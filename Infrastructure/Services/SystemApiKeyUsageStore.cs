using System.Collections.Concurrent;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace APCS.Infrastructure.Services;

/// <summary>Counts the uses of each platform key per day (UTC), and remembers the keys taken out of use for the day.</summary>
public interface ISystemApiKeyUsageStore
{
    /// <summary>
    /// Counts one more use of the key today, unless it is taken out of use or already at <paramref name="dailyLimit"/>
    /// (null for no limit). True when the use was counted.
    /// </summary>
    Task<bool> TryTakeAsync(string provider, string keyId, DateOnly day, int? dailyLimit, CancellationToken cancellationToken);

    /// <summary>Gives back one use of the key on that day.</summary>
    Task GiveBackAsync(string provider, string keyId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Takes the key out of use until the end of the day.</summary>
    Task DisableAsync(string provider, string keyId, DateOnly day, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the counts in Redis, so every server sharing it (and every restart) counts against the same daily limit.
/// While Redis cannot be reached, this server counts on its own in memory rather than stop making images.
/// </summary>
public sealed class RedisSystemApiKeyUsageStore(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> redisOptions,
    TimeProvider time,
    ILogger<RedisSystemApiKeyUsageStore> logger) : ISystemApiKeyUsageStore
{
    // Counts one use unless the key is taken out of use (KEYS[2]) or at its limit (ARGV[1], -1 for none), in one step,
    // so two servers can never both take the last use.
    private const string TakeScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then return 0 end
        local limit = tonumber(ARGV[1])
        local used = tonumber(redis.call('GET', KEYS[1]) or '0')
        if limit >= 0 and used >= limit then return 0 end
        redis.call('INCR', KEYS[1])
        redis.call('EXPIRE', KEYS[1], ARGV[2])
        return 1
        """;

    private const string GiveBackScript = """
        local used = tonumber(redis.call('GET', KEYS[1]) or '0')
        if used > 0 then redis.call('DECR', KEYS[1]) end
        return 1
        """;

    // A day's count is kept a little past its day, for anyone looking at it afterwards.
    private static readonly TimeSpan CountLifetime = TimeSpan.FromDays(2);

    private readonly InMemorySystemApiKeyUsageStore fallback = new();

    public async Task<bool> TryTakeAsync(string provider, string keyId, DateOnly day, int? dailyLimit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var taken = await redis.GetDatabase().ScriptEvaluateAsync(TakeScript,
                [CountKey(provider, keyId, day), DisabledKey(provider, keyId, day)],
                [dailyLimit ?? -1, (long)CountLifetime.TotalSeconds]);
            return (long)taken == 1;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis could not count a use of system {Provider} key {KeyId}; counting on this server only.", provider, keyId);
            return await fallback.TryTakeAsync(provider, keyId, day, dailyLimit, cancellationToken);
        }
    }

    public async Task GiveBackAsync(string provider, string keyId, DateOnly day, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().ScriptEvaluateAsync(GiveBackScript, [CountKey(provider, keyId, day)]);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis could not give back a use of system {Provider} key {KeyId}.", provider, keyId);
            await fallback.GiveBackAsync(provider, keyId, day, cancellationToken);
        }
    }

    public async Task DisableAsync(string provider, string keyId, DateOnly day, string reason, CancellationToken cancellationToken)
    {
        var endOfDay = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var left = endOfDay - time.GetUtcNow();
        try
        {
            await redis.GetDatabase().StringSetAsync(DisabledKey(provider, keyId, day), Shorten(reason),
                left > TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis could not take system {Provider} key {KeyId} out of use.", provider, keyId);
        }
        // Remembered here as well, so this server stops using the key even when Redis is away.
        await fallback.DisableAsync(provider, keyId, day, reason, cancellationToken);
    }

    private string CountKey(string provider, string keyId, DateOnly day) =>
        $"{redisOptions.Value.InstanceName}system-api-keys:{provider.ToLowerInvariant()}:{keyId}:{day:yyyy-MM-dd}:used";

    private string DisabledKey(string provider, string keyId, DateOnly day) =>
        $"{redisOptions.Value.InstanceName}system-api-keys:{provider.ToLowerInvariant()}:{keyId}:{day:yyyy-MM-dd}:disabled";

    private static string Shorten(string reason) => reason.Length <= 500 ? reason : reason[..500];
}

/// <summary>Counts on this server only: the fallback while Redis is away, and the store of the tests.</summary>
public sealed class InMemorySystemApiKeyUsageStore : ISystemApiKeyUsageStore
{
    private readonly ConcurrentDictionary<string, int> counts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> disabled = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task<bool> TryTakeAsync(string provider, string keyId, DateOnly day, int? dailyLimit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(provider, keyId, day);
        if (disabled.ContainsKey(key)) return Task.FromResult(false);
        lock (gate)
        {
            var used = counts.GetValueOrDefault(key);
            if (dailyLimit is int limit && used >= limit) return Task.FromResult(false);
            counts[key] = used + 1;
            return Task.FromResult(true);
        }
    }

    public Task GiveBackAsync(string provider, string keyId, DateOnly day, CancellationToken cancellationToken)
    {
        var key = Key(provider, keyId, day);
        lock (gate)
        {
            if (counts.GetValueOrDefault(key) > 0) counts[key]--;
        }
        return Task.CompletedTask;
    }

    public Task DisableAsync(string provider, string keyId, DateOnly day, string reason, CancellationToken cancellationToken)
    {
        disabled[Key(provider, keyId, day)] = reason;
        return Task.CompletedTask;
    }

    /// <summary>How many uses of the key are counted on that day.</summary>
    public int Used(string provider, string keyId, DateOnly day) => counts.GetValueOrDefault(Key(provider, keyId, day));

    private static string Key(string provider, string keyId, DateOnly day) => $"{provider.ToLowerInvariant()}:{keyId}:{day:yyyy-MM-dd}";
}
