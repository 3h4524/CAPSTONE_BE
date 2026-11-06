using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class CachedRagSearchService(
    IRagService inner,
    ICacheService cache,
    IOptions<RagCacheOptions> options,
    ILogger<CachedRagSearchService> logger) : IRagService
{
    private static readonly TimeSpan GenerationTtl = TimeSpan.FromDays(30);

    private readonly RagCacheOptions settings = options.Value;

    public Task<Guid> IngestAsync(RagIngestRequest request, CancellationToken cancellationToken = default) =>
        inner.IngestAsync(request, cancellationToken);

    public async Task ProcessIngestAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.ProcessIngestAsync(documentId, cancellationToken);
        }
        finally
        {
            await StartNewGenerationAsync();
        }
    }

    public async Task<bool> ActivateDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var activated = await inner.ActivateDocumentAsync(documentId, cancellationToken);
        if (activated)
            await StartNewGenerationAsync();
        return activated;
    }

    public Task<IReadOnlyList<RagAdminDocumentDto>> GetAdminDocumentsAsync(string? status, string? sourceId, CancellationToken cancellationToken = default) =>
        inner.GetAdminDocumentsAsync(status, sourceId, cancellationToken);

    public Task<RagAdminDocumentDetailDto?> GetAdminDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        inner.GetAdminDocumentAsync(documentId, cancellationToken);

    public async Task<IReadOnlyList<RagSearchResultDto>> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.Enabled || !RagCacheKeys.IsCacheable(request))
            return await inner.SearchAsync(request, cancellationToken);

        var generation = await CurrentGenerationAsync(cancellationToken);
        if (generation is null)
            return await inner.SearchAsync(request, cancellationToken);

        var key = RagCacheKeys.Search(generation, request);
        var cached = await TryGetAsync(key, cancellationToken);
        if (cached is not null)
            return cached;

        var results = await inner.SearchAsync(request, cancellationToken);
        await TrySetAsync(key, results, cancellationToken);
        return results;
    }

    private async Task<string?> CurrentGenerationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var generation = await cache.GetAsync<string>(RagCacheKeys.GenerationKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(generation))
                return generation;
            generation = Guid.NewGuid().ToString("N");
            await cache.SetAsync(RagCacheKeys.GenerationKey, generation, GenerationTtl, cancellationToken);
            return generation;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("RAG cache generation lookup failed with {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    private async Task StartNewGenerationAsync()
    {
        if (!settings.Enabled)
            return;
        try
        {
            await cache.SetAsync(RagCacheKeys.GenerationKey, Guid.NewGuid().ToString("N"), GenerationTtl, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("RAG cache invalidation failed with {ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task<IReadOnlyList<RagSearchResultDto>?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetAsync<RagSearchResultDto[]>(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("RAG search cache read failed with {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    private async Task TrySetAsync(string key, IReadOnlyList<RagSearchResultDto> results, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(key, results.ToArray(), TimeSpan.FromMinutes(settings.SearchTtlMinutes), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("RAG search cache write failed with {ExceptionType}", exception.GetType().Name);
        }
    }
}
