using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        var chunks = SplitIntoChunks(normalizedContent, NormalizeContentType(request.ContentType)).ToArray();
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
            var chunks = SplitIntoChunks(document.IngestionContent, document.ContentType).ToArray();
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
        var execution = await ExecuteSearchAsync(request, cancellationToken);
        return execution.Candidates.Select(ToSearchResult).ToArray();
    }

    public async Task<RagSearchDiagnosticsDto> SearchWithDiagnosticsAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var execution = await ExecuteSearchAsync(request, cancellationToken);
        stopwatch.Stop();

        var settings = retrievalOptions.Value;
        var results = execution.Candidates.Select(candidate => new RagSearchDiagnosticResultDto(
            ToSearchResult(candidate),
            SemanticScore(candidate, settings),
            candidate.LexicalScore,
            ViralScore(candidate.Metadata),
            FreshnessScore(candidate.Metadata, candidate.UpdatedAt, timeProvider.GetUtcNow(), settings.FreshnessHalfLifeDays),
            candidate.RankScore)).ToArray();
        var bestSemanticScore = results.Length == 0 ? 0 : results.Max(result => result.SemanticScore);
        var bestLexicalScore = results.Length == 0 ? 0 : results.Max(result => result.LexicalScore);
        var bestFinalScore = results.Length == 0 ? 0 : results.Max(result => result.FinalScore);
        var status = results.Length == 0
            ? "no_results"
            : bestSemanticScore < settings.MinimumSimilarity && bestLexicalScore <= 0
                ? "low_confidence"
                : "retrieved";
        var confidence = results.Length == 0
            ? "none"
            : status == "low_confidence"
                ? "low"
                : bestSemanticScore >= 0.75
                    ? "high"
                    : "medium";
        var appliedFilters = BuildAppliedFilters(request);
        var warnings = BuildSearchWarnings(request, execution, results.Length, bestSemanticScore, bestLexicalScore, settings.MinimumSimilarity);

        return new RagSearchDiagnosticsDto(
            request.Query,
            Math.Clamp(request.TopK, 1, 50),
            execution.CandidateLimit,
            execution.VectorCandidateCount,
            execution.LexicalCandidateCount,
            execution.MetadataMatchedCandidateCount,
            execution.RerankedCandidateCount,
            stopwatch.Elapsed.TotalMilliseconds,
            results,
            execution.CandidatesBeforeMetadataFilter,
            execution.MetadataFilteredCandidateCount,
            execution.ThresholdRejectedCandidateCount,
            bestSemanticScore,
            bestLexicalScore,
            bestFinalScore,
            status,
            confidence,
            appliedFilters,
            warnings);
    }

    public async Task<RagEvaluationResponse> EvaluateAsync(
        RagEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Cases is null || request.Cases.Count == 0)
            throw new ArgumentException("At least one evaluation case is required.", nameof(request));
        if (request.Cases.Count > 100)
            throw new ArgumentException("An evaluation run cannot contain more than 100 cases.", nameof(request));

        var topK = Math.Clamp(request.TopK, 1, 50);
        var results = new List<RagEvaluationCaseResultDto>(request.Cases.Count);
        foreach (var evaluationCase in request.Cases)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(evaluationCase.Id);
            ArgumentNullException.ThrowIfNull(evaluationCase.Search);
            var contentType = ResolveEvaluationContentType(evaluationCase);
            var searchRequest = evaluationCase.Search with
            {
                TopK = topK,
                ContentType = contentType == "mixed" ? null : contentType,
            };
            var diagnostics = await SearchWithDiagnosticsAsync(searchRequest, cancellationToken);
            var expectedMatch = diagnostics.Results.Any(result => MatchesExpected(result.Result, evaluationCase));
            var hasExpectedEvidence = evaluationCase.ExpectedDocumentId is not null
                || !string.IsNullOrWhiteSpace(evaluationCase.ExpectedSourceId)
                || !string.IsNullOrWhiteSpace(evaluationCase.ExpectedText);
            var retrievalScore = expectedMatch ? 100d : 0d;
            var citationScore = hasExpectedEvidence ? retrievalScore : 0d;
            var contextCoverageScore = string.IsNullOrWhiteSpace(evaluationCase.ExpectedText)
                ? (expectedMatch ? 100d : 0d)
                : diagnostics.Results.Any(result => result.Result.Content.Contains(evaluationCase.ExpectedText, StringComparison.OrdinalIgnoreCase)) ? 100d : 0d;
            var verdict = expectedMatch ? "pass" : diagnostics.Results.Count == 0 ? "fail" : "review";
            results.Add(new RagEvaluationCaseResultDto(
                evaluationCase.Id,
                evaluationCase.Category,
                searchRequest.Query,
                verdict,
                expectedMatch,
                retrievalScore,
                citationScore,
                contextCoverageScore,
                diagnostics.DurationMs,
                expectedMatch ? null : diagnostics.Results.Count == 0 ? "No results retrieved." : "Expected evidence was not retrieved.",
                diagnostics.Results,
                contentType,
                diagnostics));
        }

        var completedAt = timeProvider.GetUtcNow();
        var typeMetrics = results
            .GroupBy(result => result.ContentType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RagEvaluationTypeMetricsDto(
                group.Key,
                group.Count(),
                group.Average(result => result.RetrievalScore),
                group.Average(result => result.CitationScore),
                group.Average(result => result.ContextCoverageScore),
                group.Average(result => result.ExpectedMatch ? 100d : 0d),
                group.Average(result => result.LatencyMs)))
            .OrderBy(metric => metric.ContentType)
            .ToArray();
        return new RagEvaluationResponse(
            Guid.NewGuid().ToString("N"),
            completedAt,
            results.Count,
            results.Average(result => result.RetrievalScore),
            results.Average(result => result.CitationScore),
            results.Average(result => result.ContextCoverageScore),
            results.Count == 0 ? 0 : results.Count(result => result.ExpectedMatch) * 100d / results.Count,
            results.Count == 0 ? 0 : results.Average(result => result.LatencyMs),
            results,
            typeMetrics);
    }

    public async Task<RagCorpusHealthResponse> GetCorpusHealthAsync(
        string? contentType,
        int staleDays = 30,
        CancellationToken cancellationToken = default)
    {
        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? null
            : NormalizeContentType(contentType);
        if (staleDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(staleDays), "Stale days must be between 1 and 3650.");

        var staleCutoff = DateTime.UtcNow.AddDays(-staleDays);
        var documentQuery = dbContext.RagDocuments.AsNoTracking();
        if (normalizedContentType is not null)
            documentQuery = documentQuery.Where(document => document.ContentType == normalizedContentType);

        var documents = await documentQuery
            .Select(document => new CorpusDocumentRow(
                document.Id,
                document.ContentType,
                document.Status,
                document.UpdatedAt,
                document.IngestionMetadata,
                document.SourceId,
                document.ContentHash))
            .ToListAsync(cancellationToken);

        var documentIds = documents.Select(document => document.Id).ToArray();
        var chunks = documentIds.Length == 0
            ? []
            : await dbContext.RagChunks
                .AsNoTracking()
                .Where(chunk => documentIds.Contains(chunk.DocumentId))
                .Select(chunk => new CorpusChunkRow(
                    chunk.DocumentId,
                    chunk.RagEmbeddings.Count(),
                    chunk.RagEmbeddings.Count(embedding => embedding.EmbeddingModel == embeddingProvider.ModelName)))
                .ToListAsync(cancellationToken);

        var embeddingModels = documentIds.Length == 0
            ? []
            : await dbContext.RagEmbeddings
                .AsNoTracking()
                .Where(embedding => documentIds.Contains(embedding.Chunk.DocumentId))
                .Select(embedding => embedding.EmbeddingModel)
                .Distinct()
                .OrderBy(model => model)
                .ToListAsync(cancellationToken);

        var chunksByDocument = chunks.GroupBy(chunk => chunk.DocumentId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var documentHealth = documents.Select(document =>
        {
            var documentChunks = chunksByDocument.GetValueOrDefault(document.Id) ?? [];
            var metadata = InspectMetadata(document.IngestionMetadata);
            return new CorpusDocumentHealthRow(
                document,
                documentChunks.Length,
                documentChunks.Sum(chunk => chunk.EmbeddingCount),
                documentChunks.Sum(chunk => chunk.CurrentModelEmbeddingCount),
                documentChunks.Count(chunk => chunk.CurrentModelEmbeddingCount == 0),
                metadata.HasAnyValue,
                metadata.HasNiche,
                metadata.HasPlatform,
                document.Status == RagDocumentStatuses.Ready && document.UpdatedAt < staleCutoff);
        }).ToArray();

        var typeMetrics = documentHealth
            .GroupBy(document => document.Document.ContentType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RagCorpusHealthTypeMetricsDto(
                group.Key,
                group.Count(),
                group.Count(document => document.Document.Status == RagDocumentStatuses.Ready),
                group.Count(document => document.Document.Status == RagDocumentStatuses.Queued),
                group.Count(document => document.Document.Status == RagDocumentStatuses.Processing),
                group.Count(document => document.Document.Status == RagDocumentStatuses.Failed),
                group.Count(document => document.Document.Status == RagDocumentStatuses.Superseded),
                group.Sum(document => document.ChunkCount),
                group.Sum(document => document.EmbeddingCount),
                group.Sum(document => document.CurrentModelEmbeddingCount),
                group.Sum(document => document.MissingEmbeddingChunkCount),
                group.Count(document => !document.HasMetadata),
                group.Count(document => !document.HasNiche),
                group.Count(document => !document.HasPlatform),
                group.Count(document => document.IsStaleReady)))
            .OrderBy(metric => metric.ContentType)
            .ToArray();

        var duplicateContentHashGroupCount = documentHealth
            .GroupBy(document => new { document.Document.SourceId, document.Document.ContentHash })
            .Count(group => group.Count() > 1);
        var totalDocuments = documentHealth.Length;
        var readyDocuments = documentHealth.Count(document => document.Document.Status == RagDocumentStatuses.Ready);
        var queuedDocuments = documentHealth.Count(document => document.Document.Status == RagDocumentStatuses.Queued);
        var processingDocuments = documentHealth.Count(document => document.Document.Status == RagDocumentStatuses.Processing);
        var failedDocuments = documentHealth.Count(document => document.Document.Status == RagDocumentStatuses.Failed);
        var supersededDocuments = documentHealth.Count(document => document.Document.Status == RagDocumentStatuses.Superseded);
        var totalChunks = documentHealth.Sum(document => document.ChunkCount);
        var totalEmbeddings = documentHealth.Sum(document => document.EmbeddingCount);
        var currentModelEmbeddings = documentHealth.Sum(document => document.CurrentModelEmbeddingCount);
        var missingEmbeddingChunkCount = documentHealth.Sum(document => document.MissingEmbeddingChunkCount);
        var missingMetadataDocumentCount = documentHealth.Count(document => !document.HasMetadata);
        var missingNicheDocumentCount = documentHealth.Count(document => !document.HasNiche);
        var missingPlatformDocumentCount = documentHealth.Count(document => !document.HasPlatform);
        var staleReadyDocumentCount = documentHealth.Count(document => document.IsStaleReady);
        var issues = new List<RagCorpusHealthIssueDto>();
        AddHealthIssue(issues, "failed_documents", "error", failedDocuments,
            "Failed documents are not searchable and should be re-ingested.");
        AddHealthIssue(issues, "missing_embeddings", "error", missingEmbeddingChunkCount,
            $"Chunks without an embedding for the active model ({embeddingProvider.ModelName}) are not searchable by vector retrieval.");
        AddHealthIssue(issues, "stale_ready_documents", "warning", staleReadyDocumentCount,
            $"Ready documents have not been refreshed within {staleDays} days.");
        AddHealthIssue(issues, "missing_metadata", "warning", missingMetadataDocumentCount,
            "Documents do not contain ingestion metadata used by filters and reranking.");
        AddHealthIssue(issues, "duplicate_content_hashes", "info", duplicateContentHashGroupCount,
            "The same source and content hash appear in multiple document rows.");

        return new RagCorpusHealthResponse(
            timeProvider.GetUtcNow(),
            normalizedContentType,
            staleDays,
            totalDocuments,
            readyDocuments,
            queuedDocuments,
            processingDocuments,
            failedDocuments,
            supersededDocuments,
            totalChunks,
            totalEmbeddings,
            currentModelEmbeddings,
            missingEmbeddingChunkCount,
            missingMetadataDocumentCount,
            missingNicheDocumentCount,
            missingPlatformDocumentCount,
            duplicateContentHashGroupCount,
            staleReadyDocumentCount,
            embeddingModels,
            typeMetrics,
            issues);
    }

    public async Task<RagReingestResponse> ReingestAsync(
        RagReingestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedContentType = string.IsNullOrWhiteSpace(request.ContentType)
            ? null
            : NormalizeContentType(request.ContentType);
        if (request.StaleDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(request.StaleDays), "Stale days must be between 1 and 3650.");

        var maxDocuments = Math.Clamp(request.MaxDocuments, 1, 500);
        var staleCutoff = DateTime.UtcNow.AddDays(-request.StaleDays);
        var query = dbContext.RagDocuments
            .Where(document => normalizedContentType == null || document.ContentType == normalizedContentType);

        if (request.DocumentIds is { Count: > 0 })
        {
            var documentIds = request.DocumentIds.Distinct().Take(maxDocuments).ToArray();
            query = query.Where(document => documentIds.Contains(document.Id));
        }
        else
        {
            var selectFailed = request.FailedOnly;
            var selectStale = request.StaleOnly;
            var selectMissingEmbeddings = request.MissingEmbeddingOnly;
            if (!selectFailed && !selectStale && !selectMissingEmbeddings)
            {
                selectFailed = true;
                selectStale = true;
                selectMissingEmbeddings = true;
            }

            query = query.Where(document =>
                (selectFailed && document.Status == RagDocumentStatuses.Failed)
                || (selectStale && document.Status == RagDocumentStatuses.Ready && document.UpdatedAt < staleCutoff)
                || (selectMissingEmbeddings
                    && document.Status == RagDocumentStatuses.Ready
                    && document.RagChunks.Any(chunk => !chunk.RagEmbeddings.Any(embedding => embedding.EmbeddingModel == embeddingProvider.ModelName))));
        }

        var candidates = await query
            .OrderBy(document => document.UpdatedAt)
            .Take(maxDocuments)
            .ToListAsync(cancellationToken);
        var eligible = candidates
            .Where(document => document.Status is RagDocumentStatuses.Failed or RagDocumentStatuses.Ready)
            .ToArray();
        var now = DateTime.UtcNow;
        foreach (var document in eligible)
        {
            document.Status = RagDocumentStatuses.Queued;
            document.IngestionError = null;
            document.UpdatedAt = now;
        }

        if (eligible.Length > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var document in eligible)
            ingestionQueue.Enqueue(document.Id);

        return new RagReingestResponse(
            candidates.Count,
            eligible.Length,
            candidates.Count - eligible.Length,
            eligible.Select(document => document.Id).ToArray());
    }

    private static string ResolveEvaluationContentType(RagEvaluationCaseRequest evaluationCase)
    {
        var contentType = evaluationCase.ContentType ?? evaluationCase.Search.ContentType;
        return string.IsNullOrWhiteSpace(contentType)
            ? "mixed"
            : NormalizeContentType(contentType);
    }

    private async Task<SearchExecution> ExecuteSearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken)
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

        // Exact JSONB filters are applied before the vector candidate limit. Fuzzy niche matching
        // remains in memory because it intentionally supports partial/term overlap matching.
        if (!string.IsNullOrWhiteSpace(request.Platform))
        {
            var platformMetadata = JsonSerializer.Serialize(new { platform = request.Platform.Trim().ToLowerInvariant() });
            query = query.Where(x => EF.Functions.JsonContains(x.Chunk.Document.IngestionMetadata, platformMetadata));
        }

        if (!string.IsNullOrWhiteSpace(request.SourceType))
            query = query.Where(x => x.Chunk.Document.SourceType == request.SourceType);
        if (!string.IsNullOrWhiteSpace(request.Language))
            query = query.Where(x => x.Chunk.Document.Language == request.Language);
        if (!string.IsNullOrWhiteSpace(request.ContentType))
            query = query.Where(x => x.Chunk.Document.ContentType == NormalizeContentType(request.ContentType));

        var vectorRows = await query
            .OrderBy(x => x.Embedding.CosineDistance(queryVector))
            .Take(candidateCount)
            .Select(x => new SearchRow(
                x.ChunkId,
                x.Chunk.DocumentId,
                x.Chunk.Document.SourceId,
                x.Chunk.Document.Title,
                x.Chunk.Document.ContentType,
                x.Chunk.Content,
                x.Chunk.Document.SourceUrl,
                x.Chunk.Document.Version,
                x.Chunk.Document.Status,
                x.Chunk.Document.IngestionMetadata,
                x.Chunk.Document.UpdatedAt,
                x.Embedding.CosineDistance(queryVector),
                x.Chunk.Metadata))
            .ToListAsync(cancellationToken);
        var vectorCandidateCount = vectorRows.Count;
        var vectorCandidateIdsBeforeMetadata = vectorRows.Select(row => row.ChunkId).ToArray();
        vectorRows = vectorRows.Where(row => MatchesMetadata(row.IngestionMetadata, request)).ToList();

        var terms = TokenizeQuery(request.Query);
        var lexicalQuery = terms.Length == 0
            ? query.Where(_ => false)
            : query.Where(x => EF.Functions
                .ToTsVector("simple", x.Chunk.Content + " " + x.Chunk.Document.Title)
                .Matches(EF.Functions.PlainToTsQuery("simple", request.Query)));
        var lexicalRows = await lexicalQuery
            .Take(candidateCount)
            .Select(x => new SearchRow(
                x.ChunkId,
                x.Chunk.DocumentId,
                x.Chunk.Document.SourceId,
                x.Chunk.Document.Title,
                x.Chunk.Document.ContentType,
                x.Chunk.Content,
                x.Chunk.Document.SourceUrl,
                x.Chunk.Document.Version,
                x.Chunk.Document.Status,
                x.Chunk.Document.IngestionMetadata,
                x.Chunk.Document.UpdatedAt,
                0,
                x.Chunk.Metadata))
            .ToListAsync(cancellationToken);
        var lexicalCandidateCount = lexicalRows.Count;
        var lexicalCandidateIdsBeforeMetadata = lexicalRows.Select(row => row.ChunkId).ToArray();
        lexicalRows = lexicalRows.Where(row => MatchesMetadata(row.IngestionMetadata, request)).ToList();

        var lexicalScores = lexicalRows.ToDictionary(row => row.ChunkId, row => LexicalScore(row.Title, row.Content, terms));
        var rankedCandidates = vectorRows
            .Select(row => new Candidate(row.ChunkId, row.DocumentId, row.SourceId, row.Title, row.ContentType, row.Content,
                row.SourceUrl, row.Version, row.Status, MergeMetadata(row.IngestionMetadata, row.Metadata), row.UpdatedAt,
                Math.Max(0, 1 - row.Distance), lexicalScores.GetValueOrDefault(row.ChunkId)))
            .Concat(lexicalRows
                .Where(row => vectorRows.All(vector => vector.ChunkId != row.ChunkId))
                .Select(row => new Candidate(row.ChunkId, row.DocumentId, row.SourceId, row.Title, row.ContentType, row.Content,
                    row.SourceUrl, row.Version, row.Status, MergeMetadata(row.IngestionMetadata, row.Metadata), row.UpdatedAt,
                    0, lexicalScores[row.ChunkId])))
            .Select(candidate => candidate with { RankScore = Rerank(candidate, settings, timeProvider.GetUtcNow()) })
            .OrderByDescending(candidate => candidate.RankScore)
            .ThenByDescending(candidate => candidate.VectorSimilarity)
            .ToArray();
        var thresholdRejectedCandidateCount = rankedCandidates.Count(candidate =>
            candidate.VectorSimilarity < settings.MinimumSimilarity && candidate.LexicalScore <= 0);
        var candidates = rankedCandidates
            .Where(candidate => candidate.VectorSimilarity >= settings.MinimumSimilarity || candidate.LexicalScore > 0)
            .ToArray();
        var rerankedCandidateCount = candidates.Length;
        var finalCandidates = candidates
            .GroupBy(candidate => candidate.DocumentId)
            .SelectMany(group => group.Take(settings.MaxChunksPerDocument))
            .OrderByDescending(candidate => candidate.RankScore)
            .Take(topK)
            .ToArray();

        var metadataMatchedCandidateCount = vectorRows.Select(row => row.ChunkId)
            .Concat(lexicalRows.Select(row => row.ChunkId))
            .Distinct()
            .Count();
        var candidatesBeforeMetadataFilter = vectorCandidateIdsBeforeMetadata
            .Concat(lexicalCandidateIdsBeforeMetadata)
            .Distinct()
            .Count();
        var metadataFilteredCandidateCount = Math.Max(0, candidatesBeforeMetadataFilter - metadataMatchedCandidateCount);
        return new SearchExecution(candidateCount, vectorCandidateCount, lexicalCandidateCount,
            candidatesBeforeMetadataFilter, metadataFilteredCandidateCount, thresholdRejectedCandidateCount,
            metadataMatchedCandidateCount, rerankedCandidateCount, finalCandidates);
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

    private static IEnumerable<string> SplitIntoChunks(string content, string contentType)
    {
        if (string.Equals(contentType, RagContentTypes.Policy, StringComparison.OrdinalIgnoreCase))
        {
            var sections = Regex.Split(content, @"(?m)(?=^\s*#{1,6}\s+)")
                .Where(section => !string.IsNullOrWhiteSpace(section));
            foreach (var section in sections)
            {
                foreach (var chunk in SplitTextIntoChunks(section))
                    yield return chunk;
            }

            yield break;
        }

        foreach (var chunk in SplitTextIntoChunks(content))
            yield return chunk;
    }

    private static IEnumerable<string> SplitTextIntoChunks(string content)
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

    private static string Normalize(string content) => string.Join('\n', content
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        .Where(line => line.Length > 0));

    private static string ResolveSourceId(RagIngestRequest request, string sourceType) =>
        string.IsNullOrWhiteSpace(request.SourceId)
            ? string.IsNullOrWhiteSpace(request.SourceUrl) ? sourceType : request.SourceUrl.Trim()
            : request.SourceId.Trim();

    private static int EstimateTokenCount(string content) => Math.Max(1,
        content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length * 4 / 3);

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
        string SourceId,
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

    private sealed record SearchRow(
        Guid ChunkId,
        Guid DocumentId,
        string SourceId,
        string Title,
        string ContentType,
        string Content,
        string? SourceUrl,
        string? Version,
        string Status,
        string IngestionMetadata,
        DateTime UpdatedAt,
        double Distance,
        string Metadata);

    private sealed record SearchExecution(
        int CandidateLimit,
        int VectorCandidateCount,
        int LexicalCandidateCount,
        int CandidatesBeforeMetadataFilter,
        int MetadataFilteredCandidateCount,
        int ThresholdRejectedCandidateCount,
        int MetadataMatchedCandidateCount,
        int RerankedCandidateCount,
        IReadOnlyList<Candidate> Candidates);

    private static RagSearchResultDto ToSearchResult(Candidate candidate) => new(
        candidate.ChunkId,
        candidate.DocumentId,
        candidate.Title,
        candidate.ContentType,
        candidate.Content,
        candidate.SourceUrl,
        candidate.VectorSimilarity,
        DeserializeMetadata(candidate.Metadata),
        candidate.Version,
        candidate.Status,
        candidate.SourceId);

    private static double SemanticScore(Candidate candidate, RagRetrievalOptions settings) =>
        (candidate.VectorSimilarity * (1 - settings.LexicalWeight))
        + (candidate.LexicalScore * settings.LexicalWeight);

    private static double ViralScore(string metadata) =>
        Math.Clamp(ReadDouble(metadata, "viralScore") / 100, 0, 1);

    private static bool MatchesExpected(RagSearchResultDto result, RagEvaluationCaseRequest evaluationCase) =>
        (evaluationCase.ExpectedDocumentId is Guid documentId && result.DocumentId == documentId)
        || (!string.IsNullOrWhiteSpace(evaluationCase.ExpectedSourceId)
            && string.Equals(result.SourceId, evaluationCase.ExpectedSourceId, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(evaluationCase.ExpectedText)
            && (result.Title.Contains(evaluationCase.ExpectedText, StringComparison.OrdinalIgnoreCase)
                || result.Content.Contains(evaluationCase.ExpectedText, StringComparison.OrdinalIgnoreCase)));

    private static double Rerank(Candidate candidate, RagRetrievalOptions settings, DateTimeOffset now)
    {
        var baseWeight = Math.Max(0, 1 - settings.ViralWeight - settings.FreshnessWeight);
        var semanticScore = SemanticScore(candidate, settings);
        var viralScore = ViralScore(candidate.Metadata);
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

    private static IReadOnlyList<string> BuildAppliedFilters(RagSearchRequest request)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContentType))
            filters.Add($"contentType={NormalizeContentType(request.ContentType)}");
        if (!string.IsNullOrWhiteSpace(request.SourceType))
            filters.Add($"sourceType={request.SourceType.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Language))
            filters.Add($"language={request.Language.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Platform))
            filters.Add($"platform={request.Platform.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Niche))
            filters.Add($"niche={request.Niche.Trim()}");
        if (request.MinimumViralScore is not null)
            filters.Add($"minimumViralScore={request.MinimumViralScore.Value:0.##}");
        return filters;
    }

    private static IReadOnlyList<string> BuildSearchWarnings(
        RagSearchRequest request,
        SearchExecution execution,
        int resultCount,
        double bestSemanticScore,
        double bestLexicalScore,
        double minimumSimilarity)
    {
        var warnings = new List<string>();
        var requestedTopK = Math.Clamp(request.TopK, 1, 50);

        if (execution.CandidatesBeforeMetadataFilter == 0)
        {
            warnings.Add("No ready embedding candidate matched the content type, source, language, or other exact filters.");
        }
        else if (execution.MetadataFilteredCandidateCount > 0)
        {
            warnings.Add($"{execution.MetadataFilteredCandidateCount} candidate(s) were removed by metadata filters. Verify niche, platform, and viral-score metadata.");
        }

        if (execution.ThresholdRejectedCandidateCount > 0)
        {
            warnings.Add($"{execution.ThresholdRejectedCandidateCount} candidate(s) failed the relevance threshold ({minimumSimilarity:0.00}) and had no lexical match.");
        }

        if (resultCount == 0)
        {
            warnings.Add("No context survived retrieval filters and the relevance threshold.");
        }
        else if (resultCount < requestedTopK)
        {
            warnings.Add($"Only {resultCount} of {requestedTopK} requested result(s) were returned.");
        }

        if (resultCount > 0 && bestLexicalScore <= 0)
        {
            warnings.Add("No lexical term match was found in the returned results; inspect semantic relevance.");
        }

        if (resultCount > 0 && bestSemanticScore < minimumSimilarity && bestLexicalScore <= 0)
        {
            warnings.Add("The returned context is below the configured semantic similarity threshold.");
        }

        return warnings;
    }

    private static IReadOnlyDictionary<string, object?> DeserializeMetadata(string metadata) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(metadata) ?? new Dictionary<string, object?>();

    private static void AddHealthIssue(
        ICollection<RagCorpusHealthIssueDto> issues,
        string code,
        string severity,
        int count,
        string description)
    {
        if (count > 0)
            issues.Add(new RagCorpusHealthIssueDto(code, severity, count, description));
    }

    private static MetadataHealth InspectMetadata(string metadata)
    {
        using var document = ParseMetadata(metadata);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return new MetadataHealth(false, false, false);

        return new MetadataHealth(
            root.EnumerateObject().Any(),
            HasStringValue(root, "niche"),
            HasStringValue(root, "platform"));
    }

    private static bool HasStringValue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    private sealed record CorpusDocumentRow(
        Guid Id,
        string ContentType,
        string Status,
        DateTime UpdatedAt,
        string IngestionMetadata,
        string SourceId,
        string ContentHash);

    private sealed record CorpusChunkRow(
        Guid DocumentId,
        int EmbeddingCount,
        int CurrentModelEmbeddingCount);

    private sealed record CorpusDocumentHealthRow(
        CorpusDocumentRow Document,
        int ChunkCount,
        int EmbeddingCount,
        int CurrentModelEmbeddingCount,
        int MissingEmbeddingChunkCount,
        bool HasMetadata,
        bool HasNiche,
        bool HasPlatform,
        bool IsStaleReady);

    private sealed record MetadataHealth(bool HasAnyValue, bool HasNiche, bool HasPlatform);
}
