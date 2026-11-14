using System.Text.Json;
using APCS.Application.Abstractions.Caching;

namespace APCS.Application.UnitTests.TestSupport;

/// <summary>A cache that keeps values as JSON, as the real one does, so what comes back is a copy.</summary>
internal sealed class TestMemoryCache : ICacheService
{
    private readonly Dictionary<string, string> values = [];

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        values[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        values.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        foreach (var key in values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            values.Remove(key);
        return Task.CompletedTask;
    }
}
