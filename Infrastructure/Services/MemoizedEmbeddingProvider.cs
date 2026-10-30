using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class MemoizedEmbeddingProvider(
    IRagEmbeddingProvider inner,
    ICacheService cache,
    IOptions<RagCacheOptions> options,
    ILogger<MemoizedEmbeddingProvider> logger) : IRagEmbeddingProvider
{
    private readonly RagCacheOptions settings = options.Value;

    public string ModelName => inner.ModelName;

    public int Dimensions => inner.Dimensions;

    public async Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(text))
            return await inner.CreateEmbeddingAsync(text, cancellationToken);

        var key = RagCacheKeys.Embedding(ModelName, text);
        var cached = await TryGetAsync(key, cancellationToken);
        if (cached is not null && cached.Length == Dimensions)
            return cached;

        var vector = await inner.CreateEmbeddingAsync(text, cancellationToken);
        await TrySetAsync(key, vector, cancellationToken);
        return vector;
    }

    private async Task<float[]?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetAsync<float[]>(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Embedding cache read failed with {ExceptionType}", exception.GetType().Name);
            return null;
        }
    }

    private async Task TrySetAsync(string key, float[] vector, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(key, vector, TimeSpan.FromHours(settings.EmbeddingTtlHours), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Embedding cache write failed with {ExceptionType}", exception.GetType().Name);
        }
    }
}
