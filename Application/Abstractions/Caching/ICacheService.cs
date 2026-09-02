namespace APCS.Application.Abstractions.Caching;

/// <summary>
/// Provides a technology-agnostic cache abstraction.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a cached value by key.
    /// </summary>
    Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a value in the cache with an optional expiration.
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all cached values matching a prefix.
    /// Not supported by all implementations.
    /// </summary>
    Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default);
}
