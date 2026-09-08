using System.Text.Json;
using APCS.Application.Abstractions.Caching;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements <see cref="ICacheService"/> using Redis via <see cref="IDistributedCache"/>.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly RedisOptions _options;

    public RedisCacheService(
        IDistributedCache cache,
        IOptions<RedisOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(key, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (value is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                expiration ?? TimeSpan.FromMinutes(_options.DefaultExpirationMinutes)
        };

        await _cache.SetStringAsync(key, json, cacheOptions, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return _cache.RemoveAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// IDistributedCache does not support SCAN/KEYS operations.
    /// Use explicit cache key removal instead.
    /// If prefix-based invalidation is required, consider using IConnectionMultiplexer directly.
    /// </remarks>
    public Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Prefix-based cache invalidation is not supported by IDistributedCache. " +
            "Use explicit cache keys for now.");
    }
}
