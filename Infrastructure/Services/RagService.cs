using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Rag.Common;
using APCS.Domain.Entities;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure.Services;

public sealed class RagService(
    AppDbContext dbContext,
    IRagEmbeddingProvider embeddingProvider,
    IOptions<RagRetrievalOptions> retrievalOptions,
    IRagIngestionQueue ingestionQueue,
    TimeProvider timeProvider)
    : IRagService
{
    private const int ChunkSize = 1400;
    private const int ChunkOverlap = 200;
    private static readonly TimeSpan StaleProcessingTimeout = TimeSpan.FromMinutes(5);

    public async Task<Guid> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Document content cannot be empty.", nameof(request));

        var normalizedContent = Normalize(request.Content);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent)));
        var sourceType = request.SourceType?.Trim();
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("Source type cannot be empty.", nameof(request));

        var sourceId = ResolveSourceId(request, sourceType);
        var normalizedTitle = request.Title.Trim();
        var version = string.IsNullOrWhiteSpace(request.Version) ? null : request.Version.Trim();
        var existing = await dbContext.RagDocuments
            .AsNoTracking()
            .Where(x => x.SourceId == sourceId && x.ContentHash == hash)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.Id, x.Status, x.UpdatedAt, ChunkCount = x.RagChunks.Count() })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null && existing.Status != RagDocumentStatuses.Failed)
        {
            var isStaleProcessing = existing.Status == RagDocumentStatuses.Processing
                && existing.UpdatedAt < DateTime.UtcNow.Subtract(StaleProcessingTimeout);
            var isCompleted = existing.Status == RagDocumentStatuses.Ready && existing.ChunkCount > 0;
            var isActive = existing.Status is RagDocumentStatuses.Queued or RagDocumentStatuses.Processing;
            if (isCompleted || (isActive && !isStaleProcessing))
                return existing.Id;

            var previousDocument = await dbContext.RagDocuments
                .SingleAsync(x => x.Id == existing.Id, cancellationToken);
            previousDocument.Status = RagDocumentStatuses.Superseded;
            previousDocument.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (existing is not null && existing.Status == RagDocumentStatuses.Failed)
        {
            var failedDocument = await dbContext.RagDocuments
                .SingleAsync(x => x.Id == existing.Id, cancellationToken);
            dbContext.RagDocuments.Remove(failedDocument);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var metadata = JsonSerializer.Serialize(request.Metadata ?? new Dictionary<string, object?>());
        var document = new RagDocument
        {
            Title = normalizedTitle,
            SourceType = sourceType,
            SourceId = sourceId,
            ContentType = NormalizeContentType(request.ContentType),
            SourceUrl = request.SourceUrl?.Trim(),
            Version = version,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
            ContentHash = hash,
            IngestionContent = normalizedContent,
            IngestionMetadata = metadata,
            Status = RagDocumentStatuses.Queued,
            UpdatedAt = DateTime.UtcNow
        };

        var chunks = SplitIntoChunks(normalizedContent).ToArray();
        if (chunks.Length == 0)
            throw new ArgumentException("Document content cannot be empty after normalization.", nameof(request));

        dbContext.RagDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        ingestionQueue.Enqueue(document.Id);
        return document.Id;
    }

    public async Task ProcessIngestAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var claimed = await dbContext.RagDocuments
            .Where(x => x.Id == documentId
                && (x.Status == RagDocumentStatuses.Queued
                    || (x.Status == RagDocumentStatuses.Processing
                        && x.UpdatedAt < now.Subtract(StaleProcessingTimeout))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RagDocumentStatuses.Processing)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        if (claimed == 0)
            return;

        var document = await dbContext.RagDocuments
            .SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document is null)
            return;

        try
        {
            var chunks = SplitIntoChunks(document.IngestionContent).ToArray();
            if (chunks.Length == 0)
                throw new InvalidOperationException("Document content cannot be empty after normalization.");

            var oldChunks = await dbContext.RagChunks
                .Where(x => x.DocumentId == document.Id)
                .ToListAsync(cancellationToken);
            if (oldChunks.Count > 0)
            {
                dbContext.RagChunks.RemoveRange(oldChunks);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var pendingVersions = await dbContext.RagDocuments
                .Where(x => x.Id != document.Id
                    && x.SourceId == document.SourceId
                    && x.Status == RagDocumentStatuses.Processing
                    && x.Title.ToLower() == document.Title.ToLower())
                .ToListAsync(cancellationToken);
            foreach (var pendingVersion in pendingVersions)
            {
                pendingVersion.Status = RagDocumentStatuses.Superseded;
                pendingVersion.UpdatedAt = DateTime.UtcNow;
            }

            foreach (var (content, index) in chunks.Select((content, index) => (content, index)))
            {
                var vector = await embeddingProvider.CreateEmbeddingAsync(content, cancellationToken);
                var chunk = new RagChunk
                {
                    DocumentId = document.Id,
                    Content = content,
                    ChunkIndex = index,
                    TokenCount = EstimateTokenCount(content),
                    Metadata = document.IngestionMetadata
                };
                var embedding = new RagEmbedding
                {
                    ChunkId = chunk.Id,
                    Embedding = new Vector(vector),
                    EmbeddingModel = embeddingProvider.ModelName
                };
                chunk.RagEmbeddings.Add(embedding);
                document.RagChunks.Add(chunk);
                dbContext.RagChunks.Add(chunk);
                dbContext.RagEmbeddings.Add(embedding);
            }

            // A newer ingest may have superseded this request while its embeddings were running.
            // Do not let the older request publish itself as ready after that happens.
            var currentStatus = await dbContext.RagDocuments
                .AsNoTracking()
                .Where(x => x.Id == document.Id)
                .Select(x => x.Status)
                .SingleAsync(cancellationToken);
            if (currentStatus != RagDocumentStatuses.Processing)
                return;

            var previousVersions = await dbContext.RagDocuments
                .Where(x => x.SourceId == document.SourceId
                    && x.Id != document.Id
                    && x.Status == RagDocumentStatuses.Ready)
                .ToListAsync(cancellationToken);
            foreach (var previous in previousVersions)
            {
                previous.Status = RagDocumentStatuses.Superseded;
                previous.UpdatedAt = DateTime.UtcNow;
            }

            document.Status = RagDocumentStatuses.Ready;
            document.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            dbContext.ChangeTracker.Clear();
            var failedDocument = await dbContext.RagDocuments
                .SingleOrDefaultAsync(x => x.Id == documentId, CancellationToken.None);
            if (failedDocument is not null)
            {
                failedDocument.Status = RagDocumentStatuses.Failed;
                failedDocument.IngestionError = exception.Message[..Math.Min(exception.Message.Length, 2000)];
                failedDocument.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    public async Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Search query cannot be empty.", nameof(request));

        var topK = Math.Clamp(request.TopK, 1, 50);
        var settings = retrievalOptions.Value;
        var candidateCount = Math.Min(500, topK * settings.CandidateMultiplier
            * (request.Platform is not null || request.Niche is not null || request.MinimumViralScore is not null ? 4 : 1));
        var queryVector = new Vector(await embeddingProvider.CreateEmbeddingAsync(request.Query, cancellationToken));

        var query = dbContext.RagEmbeddings
            .AsNoTracking()
            .Where(x => x.EmbeddingModel == embeddingProvider.ModelName
                && x.Chunk.Document.Status == RagDocumentStatuses.Ready);

        if (!string.IsNullOrWhiteSpace(request.SourceType))
            query = query.Where(x => x.Chunk.Document.SourceType == request.SourceType);
        if (!string.IsNullOrWhiteSpace(request.Language))
            query = query.Where(x => x.Chunk.Document.Language == request.Language);
        if (!string.IsNullOrWhiteSpace(request.ContentType))
            query = query.Where(x => x.Chunk.Document.ContentType == NormalizeContentType(request.ContentType));
        if (request.VideoExperience is not null)
        {
            if (!string.Equals(request.ContentType, RagContentTypes.Video, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A video experience scope requires video content.", nameof(request));
            query = ApplyVideoExperienceScope(query, request.VideoExperience);
        }

        var vectorRows = await query
            .OrderBy(x => x.Embedding.CosineDistance(queryVector))
            .Take(candidateCount)
            .Select(x => new
            {
                x.ChunkId,
                DocumentId = x.Chunk.DocumentId,
                Title = x.Chunk.Document.Title,
                ContentType = x.Chunk.Document.ContentType,
                x.Chunk.Content,
                x.Chunk.Document.SourceUrl,
                x.Chunk.Document.Version,
                x.Chunk.Document.Status,
                x.Chunk.Document.IngestionMetadata,
                x.Chunk.Document.UpdatedAt,
                Distance = x.Embedding.CosineDistance(queryVector),
                x.Chunk.Metadata
            })
            .ToListAsync(cancellationToken);
        vectorRows = vectorRows.Where(row => MatchesMetadata(row.IngestionMetadata, request)).ToList();

        var terms = TokenizeQuery(request.Query);
        var lexicalRows = terms.Length == 0
            ? await query.Where(_ => false).Select(x => new
            {
                x.ChunkId,
                DocumentId = x.Chunk.DocumentId,
                Title = x.Chunk.Document.Title,
                ContentType = x.Chunk.Document.ContentType,
                x.Chunk.Content,
                x.Chunk.Document.SourceUrl,
                x.Chunk.Document.Version,
                x.Chunk.Document.Status,
                x.Chunk.Document.IngestionMetadata,
                x.Chunk.Document.UpdatedAt,
                x.Chunk.Metadata
            }).ToListAsync(cancellationToken)
            : await query
                .Where(x => EF.Functions
                    .ToTsVector("simple", x.Chunk.Content + " " + x.Chunk.Document.Title)
                    .Matches(EF.Functions.PlainToTsQuery("simple", request.Query)))
                .Take(candidateCount)
                .Select(x => new
                {
                    x.ChunkId,
                    DocumentId = x.Chunk.DocumentId,
                    Title = x.Chunk.Document.Title,
                    ContentType = x.Chunk.Document.ContentType,
                    x.Chunk.Content,
                    x.Chunk.Document.SourceUrl,
                    x.Chunk.Document.Version,
                    x.Chunk.Document.Status,
                    x.Chunk.Document.IngestionMetadata,
                    x.Chunk.Document.UpdatedAt,
                    x.Chunk.Metadata
                })
                .ToListAsync(cancellationToken);
        lexicalRows = lexicalRows.Where(row => MatchesMetadata(row.IngestionMetadata, request)).ToList();

        var lexicalScores = lexicalRows.ToDictionary(
            row => row.ChunkId,
            row => LexicalScore(row.Title, row.Content, terms));

        var candidates = vectorRows
            .Select(row => new Candidate(
                row.ChunkId,
                row.DocumentId,
                row.Title,
                row.ContentType,
                row.Content,
                row.SourceUrl,
                row.Version,
                row.Status,
                MergeMetadata(row.IngestionMetadata, row.Metadata),
                row.UpdatedAt,
                Math.Max(0, 1 - row.Distance),
                lexicalScores.GetValueOrDefault(row.ChunkId)))
            .Concat(lexicalRows
                .Where(row => vectorRows.All(vector => vector.ChunkId != row.ChunkId))
                .Select(row => new Candidate(
                    row.ChunkId,
                    row.DocumentId,
                    row.Title,
                    row.ContentType,
                    row.Content,
                    row.SourceUrl,
                    row.Version,
                    row.Status,
                    MergeMetadata(row.IngestionMetadata, row.Metadata),
                    row.UpdatedAt,
                    0,
                    lexicalScores[row.ChunkId])))
            .Select(candidate => candidate with
            {
                RankScore = Rerank(candidate, settings, timeProvider.GetUtcNow())
            })
            .Where(candidate => candidate.VectorSimilarity >= settings.MinimumSimilarity
                || candidate.LexicalScore > 0)
            .OrderByDescending(candidate => candidate.RankScore)
            .ThenByDescending(candidate => candidate.VectorSimilarity)
            .GroupBy(candidate => candidate.DocumentId)
            .SelectMany(group => group.Take(settings.MaxChunksPerDocument))
            .OrderByDescending(candidate => candidate.RankScore)
            .Take(topK)
            .ToArray();

        return candidates.Select(row => new RagSearchResultDto(
            row.ChunkId,
            row.DocumentId,
            row.Title,
            row.ContentType,
            row.Content,
            row.SourceUrl,
            row.RankScore,
            DeserializeMetadata(row.Metadata),
            row.Version,
            row.Status)).ToArray();
    }

    public async Task<IReadOnlyList<RagAdminDocumentDto>> GetAdminDocumentsAsync(
        string? status,
        string? sourceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.RagDocuments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            if (!RagDocumentStatuses.IsSupported(normalizedStatus))
                throw new ArgumentException($"Unsupported RAG document status: {status}.", nameof(status));
            query = query.Where(x => x.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(sourceId))
            query = query.Where(x => x.SourceId == sourceId.Trim());

        return await query
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new RagAdminDocumentDto(
                x.Id,
                x.SourceId,
                x.Title,
                x.SourceType,
                x.ContentType,
                x.SourceUrl,
                x.Version,
                x.Language,
                x.Status,
                x.IngestionError,
                x.RagChunks.Count,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<RagAdminDocumentDetailDto?> GetAdminDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.RagDocuments
            .AsNoTracking()
            .Where(x => x.Id == documentId)
            .Select(x => new RagAdminDocumentDto(
                x.Id,
                x.SourceId,
                x.Title,
                x.SourceType,
                x.ContentType,
                x.SourceUrl,
                x.Version,
                x.Language,
                x.Status,
                x.IngestionError,
                x.RagChunks.Count,
                x.CreatedAt,
                x.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
            return null;

        var chunkRows = await dbContext.RagChunks
            .AsNoTracking()
            .Where(x => x.DocumentId == documentId)
            .OrderBy(x => x.ChunkIndex)
            .Select(x => new
            {
                x.Id,
                x.ChunkIndex,
                x.TokenCount,
                x.Content,
                x.Metadata
            })
            .ToListAsync(cancellationToken);

        var chunks = chunkRows
            .Select(x => new RagAdminChunkDto(
                x.Id,
                x.ChunkIndex,
                x.TokenCount,
                x.Content,
                DeserializeMetadata(x.Metadata)))
            .ToArray();

        return new RagAdminDocumentDetailDto(document, chunks);
    }

    public async Task<bool> ActivateDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await dbContext.RagDocuments
            .SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken);
        if (document is null || document.Status == RagDocumentStatuses.Failed || document.Status == RagDocumentStatuses.Processing)
            return false;

        var chunkCount = await dbContext.RagChunks
            .CountAsync(x => x.DocumentId == documentId, cancellationToken);
        if (chunkCount == 0)
            return false;

        var versions = await dbContext.RagDocuments
            .Where(x => x.SourceId == document.SourceId && x.Id != documentId && x.Status == RagDocumentStatuses.Ready)
            .ToListAsync(cancellationToken);
        foreach (var version in versions)
        {
            version.Status = RagDocumentStatuses.Superseded;
            version.UpdatedAt = DateTime.UtcNow;
        }

        document.Status = RagDocumentStatuses.Ready;
        document.IngestionError = null;
        document.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RagVideoExperienceDto?> GetVideoExperienceAsync(Guid documentId, Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.RagDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId
            && x.ContentType == RagContentTypes.Video && x.SourceType == "video-experience", cancellationToken);
        if (document is null || !MetadataMatchesOwner(document.IngestionMetadata, ownerId))
            return null;
        using var metadata = ParseMetadata(document.IngestionMetadata);
        var status = ReadString(metadata, "experienceStatus") ?? RagExperienceStatuses.Candidate;
        return new(document.Id, status, document.Version, document.IngestionContent);
    }

    public async Task<RagVideoExperienceDto?> SetVideoExperienceStatusAsync(Guid documentId, Guid ownerId, string status,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedStatus) || !RagExperienceStatuses.IsSupported(normalizedStatus))
            throw new ArgumentException("Unsupported video experience status.", nameof(status));

        var document = await dbContext.RagDocuments.SingleOrDefaultAsync(x => x.Id == documentId
            && x.ContentType == RagContentTypes.Video && x.SourceType == "video-experience", cancellationToken);
        if (document is null || !MetadataMatchesOwner(document.IngestionMetadata, ownerId))
            return null;
        if (normalizedStatus == RagExperienceStatuses.Verified)
        {
            var chunkCount = await dbContext.RagChunks.CountAsync(x => x.DocumentId == documentId, cancellationToken);
            if (document.Status != RagDocumentStatuses.Ready || chunkCount == 0)
                return null;
            var superseded = await dbContext.RagDocuments
                .Where(x => x.SourceId == document.SourceId && x.Id != document.Id && x.Status == RagDocumentStatuses.Ready)
                .ToListAsync(cancellationToken);
            foreach (var previous in superseded)
            {
                previous.Status = RagDocumentStatuses.Superseded;
                previous.UpdatedAt = DateTime.UtcNow;
            }
        }

        var metadata = DeserializeMutableMetadata(document.IngestionMetadata);
        metadata["experienceStatus"] = normalizedStatus;
        document.IngestionMetadata = JsonSerializer.Serialize(metadata);
        document.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(document.Id, normalizedStatus, document.Version, document.IngestionContent);
    }

    public async Task<RagVideoExperienceDto?> ReviseVideoExperienceAsync(Guid documentId, Guid ownerId, string content,
        CancellationToken cancellationToken = default)
    {
        var normalizedContent = Normalize(content);
        if (normalizedContent.Length is < 1 or > 1500)
            throw new ArgumentException("Video experience content must contain 1-1500 characters.", nameof(content));
        var document = await dbContext.RagDocuments.SingleOrDefaultAsync(x => x.Id == documentId
            && x.ContentType == RagContentTypes.Video && x.SourceType == "video-experience", cancellationToken);
        if (document is null || !MetadataMatchesOwner(document.IngestionMetadata, ownerId))
            return null;

        var revision = int.TryParse(document.Version, out var currentRevision) ? currentRevision + 1 : 2;
        var oldMetadata = DeserializeMutableMetadata(document.IngestionMetadata);
        oldMetadata["experienceStatus"] = RagExperienceStatuses.Retired;
        oldMetadata["replacedByRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        document.IngestionMetadata = JsonSerializer.Serialize(oldMetadata);
        document.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        oldMetadata.Remove("replacedByRevision");
        oldMetadata["experienceStatus"] = RagExperienceStatuses.Candidate;
        oldMetadata["replacesLessonId"] = documentId.ToString();
        var sourceId = $"{document.SourceId}:r{revision}";
        var newDocumentId = await IngestAsync(new RagIngestRequest(document.Title, document.SourceType, document.SourceUrl,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture), document.Language, normalizedContent,
            oldMetadata, document.ContentType, sourceId), cancellationToken);
        return new(newDocumentId, RagExperienceStatuses.Candidate,
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture), normalizedContent);
    }

    private static IEnumerable<string> SplitIntoChunks(string content)
    {
        var start = 0;
        while (start < content.Length)
        {
            var length = Math.Min(ChunkSize, content.Length - start);
            var end = start + length;
            if (end < content.Length)
            {
                var boundary = content.LastIndexOfAny([' ', '\n', '.', ',', ';', ':'], end - 1, Math.Min(250, length));
                if (boundary > start + ChunkSize / 2)
                    end = boundary + 1;
            }

            yield return content[start..end].Trim();
            if (end >= content.Length) yield break;
            start = Math.Max(start + 1, end - ChunkOverlap);
        }
    }

    private static string Normalize(string content) => string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ResolveSourceId(RagIngestRequest request, string sourceType) =>
        string.IsNullOrWhiteSpace(request.SourceId)
            ? string.IsNullOrWhiteSpace(request.SourceUrl) ? sourceType : request.SourceUrl.Trim()
            : request.SourceId.Trim();

    private static int EstimateTokenCount(string content) => Math.Max(1, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 4 / 3);

    private static string[] TokenizeQuery(string query) => query
        .Split([' ', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
        .Select(term => term.Trim())
        .Where(term => term.Length >= 2)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(12)
        .ToArray();

    private static double LexicalScore(string title, string content, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;

        var haystack = $"{title} {content}";
        var matched = terms.Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
        return (double)matched / terms.Count;
    }

    private sealed record Candidate(
        Guid ChunkId,
        Guid DocumentId,
        string Title,
        string ContentType,
        string Content,
        string? SourceUrl,
        string? Version,
        string Status,
        string Metadata,
        DateTime UpdatedAt,
        double VectorSimilarity,
        double LexicalScore,
        double RankScore = 0);

    private static double Rerank(Candidate candidate, RagRetrievalOptions settings, DateTimeOffset now)
    {
        var baseWeight = Math.Max(0, 1 - settings.ViralWeight - settings.FreshnessWeight);
        var semanticScore = (candidate.VectorSimilarity * (1 - settings.LexicalWeight))
            + (candidate.LexicalScore * settings.LexicalWeight);
        var viralScore = Math.Clamp(ReadDouble(candidate.Metadata, "viralScore") / 100, 0, 1);
        var freshnessScore = FreshnessScore(candidate.Metadata, candidate.UpdatedAt, now, settings.FreshnessHalfLifeDays);
        return (semanticScore * baseWeight)
            + (viralScore * settings.ViralWeight)
            + (freshnessScore * settings.FreshnessWeight);
    }

    private static bool MatchesMetadata(string metadata, RagSearchRequest request)
    {
        if (request.Platform is null && request.Niche is null && request.MinimumViralScore is null)
            return true;

        using var document = ParseMetadata(metadata);
        if (request.Platform is not null && !string.Equals(ReadString(document, "platform"), request.Platform, StringComparison.OrdinalIgnoreCase))
            return false;
        if (request.Niche is not null && !NicheMatches(ReadString(document, "niche"), request.Niche))
            return false;
        return request.MinimumViralScore is null || ReadDouble(document, "viralScore") >= request.MinimumViralScore.Value;
    }

    private static IQueryable<RagEmbedding> ApplyVideoExperienceScope(IQueryable<RagEmbedding> query,
        VideoExperienceSearchRequest scope)
    {
        var ownerAndStatus = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ownerId"] = scope.OwnerId.ToString(),
            ["experienceStatus"] = RagExperienceStatuses.Verified
        });
        query = query.Where(x => EF.Functions.JsonContains(x.Chunk.Document.IngestionMetadata, ownerAndStatus));
        if (!string.IsNullOrWhiteSpace(scope.TemplateCode))
        {
            var templateCode = JsonSerializer.Serialize(new Dictionary<string, string> { ["templateCode"] = scope.TemplateCode.Trim() });
            query = query.Where(x => EF.Functions.JsonContains(x.Chunk.Document.IngestionMetadata, templateCode));
        }
        if (!string.IsNullOrWhiteSpace(scope.ModelKey))
        {
            var modelKey = JsonSerializer.Serialize(new Dictionary<string, string> { ["modelKey"] = scope.ModelKey.Trim() });
            query = query.Where(x => EF.Functions.JsonContains(x.Chunk.Document.IngestionMetadata, modelKey));
        }
        if (!string.IsNullOrWhiteSpace(scope.PrimaryAction))
        {
            var primaryAction = JsonSerializer.Serialize(new Dictionary<string, string> { ["primaryAction"] = scope.PrimaryAction.Trim() });
            query = query.Where(x => EF.Functions.JsonContains(x.Chunk.Document.IngestionMetadata, primaryAction));
        }
        return query;
    }

    private static bool MetadataMatchesOwner(string metadata, Guid ownerId)
    {
        using var document = ParseMetadata(metadata);
        return string.Equals(ReadString(document, "ownerId"), ownerId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> DeserializeMutableMetadata(string metadata)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static double FreshnessScore(string metadata, DateTime updatedAt, DateTimeOffset now, int halfLifeDays)
    {
        using var document = ParseMetadata(metadata);
        var collectedAt = ReadDateTimeOffset(document, "collectedAtUtc") ?? new DateTimeOffset(updatedAt, TimeSpan.Zero);
        var ageDays = Math.Max(0, (now - collectedAt).TotalDays);
        return Math.Clamp(Math.Exp(-Math.Log(2) * ageDays / halfLifeDays), 0, 1);
    }

    private static JsonDocument ParseMetadata(string metadata)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(metadata) ? "{}" : metadata);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static string? ReadString(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool NicheMatches(string? storedNiche, string requestedNiche)
    {
        if (string.IsNullOrWhiteSpace(storedNiche))
            return false;
        if (storedNiche.Contains(requestedNiche, StringComparison.OrdinalIgnoreCase)
            || requestedNiche.Contains(storedNiche, StringComparison.OrdinalIgnoreCase))
            return true;

        var requestedTerms = TokenizeQuery(requestedNiche);
        var storedTerms = TokenizeQuery(storedNiche);
        return requestedTerms.Any(term => storedTerms.Contains(term, StringComparer.OrdinalIgnoreCase));
    }

    private static double ReadDouble(string metadata, string name)
    {
        using var document = ParseMetadata(metadata);
        return ReadDouble(document, name);
    }

    private static double ReadDouble(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static DateTimeOffset? ReadDateTimeOffset(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string MergeMetadata(string documentMetadata, string chunkMetadata)
    {
        var merged = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(documentMetadata) ?? [];
        var chunk = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(chunkMetadata) ?? [];
        foreach (var item in chunk)
            merged[item.Key] = item.Value;
        return JsonSerializer.Serialize(merged);
    }

    private static string NormalizeContentType(string? value)
    {
        var contentType = string.IsNullOrWhiteSpace(value) ? RagContentTypes.Listing : value.Trim().ToLowerInvariant();
        if (!RagContentTypes.IsSupported(contentType))
            throw new ArgumentException($"Unsupported RAG content type: {value}.", nameof(value));
        return contentType;
    }

    private static IReadOnlyDictionary<string, object?> DeserializeMetadata(string metadata) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? new Dictionary<string, object?>();
}
