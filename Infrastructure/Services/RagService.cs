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
    IRagIngestionQueue ingestionQueue)
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
            .Select(x => new { x.Id, x.Status, x.UpdatedAt, ChunkCount = x.Chunks.Count() })
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
                chunk.Embeddings.Add(embedding);
                document.Chunks.Add(chunk);
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
        var candidateCount = Math.Min(500, topK * settings.CandidateMultiplier);
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
                Distance = x.Embedding.CosineDistance(queryVector),
                x.Chunk.Metadata
            })
            .ToListAsync(cancellationToken);

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
                    x.Chunk.Metadata
                })
                .ToListAsync(cancellationToken);

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
                row.Metadata,
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
                    row.Metadata,
                    0,
                    lexicalScores[row.ChunkId])))
            .Select(candidate => candidate with
            {
                RankScore = (candidate.VectorSimilarity * (1 - settings.LexicalWeight))
                    + (candidate.LexicalScore * settings.LexicalWeight)
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
                x.Chunks.Count,
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
                x.Chunks.Count,
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
        double VectorSimilarity,
        double LexicalScore,
        double RankScore = 0);

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
