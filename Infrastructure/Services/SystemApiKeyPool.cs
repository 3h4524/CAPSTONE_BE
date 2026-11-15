using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Hands out the platform's keys, by the provider's <see cref="SystemApiKeyProviderOptions.Strategy"/>: every request
/// starts at the first key (<see cref="SystemApiKeyStrategies.Fill"/>, the default), or at the key after the one the last
/// request started at (<see cref="SystemApiKeyStrategies.Rotate"/>). From there a key used up or refused for the day, or
/// with every place taken, is passed over for the next one. The daily counts live in
/// <see cref="ISystemApiKeyUsageStore"/> (shared by every server), the requests at a time on this server only.
/// </summary>
public sealed class SystemApiKeyPool : ISystemApiKeyPool
{
    private readonly Dictionary<string, ProviderKeys> providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISystemApiKeyUsageStore usage;
    private readonly TimeProvider time;
    private readonly ILogger<SystemApiKeyPool> logger;

    public SystemApiKeyPool(IOptions<SystemApiKeysOptions> options, ISystemApiKeyUsageStore usage, TimeProvider time,
        ILogger<SystemApiKeyPool> logger)
    {
        this.usage = usage;
        this.time = time;
        this.logger = logger;
        foreach (var (provider, settings) in options.Value.Providers)
        {
            var keys = settings.Keys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => new PoolKey(KeyIdOf(key), key.Trim(),
                    settings.MaxConcurrentPerKey is int slots and > 0 ? new SemaphoreSlim(slots, slots) : null))
                .ToArray();
            if (keys.Length > 0)
                providers[provider] = new ProviderKeys(provider.ToLowerInvariant(), keys, settings.DailyLimitPerKey, Rotates(provider, settings.Strategy));
        }
    }

    public bool HasKeys(string provider) => providers.ContainsKey(provider);

    public string? FindKey(string provider, string keyId) =>
        providers.TryGetValue(provider, out var keys) ? keys.Keys.FirstOrDefault(key => key.Id == keyId)?.Value : null;

    public string? AnyKey(string provider) => providers.TryGetValue(provider, out var keys) ? keys.Keys[0].Value : null;

    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>();

    public Task<ISystemApiKeyLease?> AcquireAsync(string provider, CancellationToken cancellationToken = default) =>
        AcquireAsync(provider, NoKeys, cancellationToken);

    public async Task<ISystemApiKeyLease?> AcquireAsync(string provider, IReadOnlySet<string> skipKeyIds,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(provider, out var pool)) return null;

        while (true)
        {
            var day = Today();
            var start = pool.Rotate ? (int)((uint)Interlocked.Increment(ref pool.Next) % (uint)pool.Keys.Length) : 0;
            PoolKey? busy = null;
            for (var i = 0; i < pool.Keys.Length; i++)
            {
                var key = pool.Keys[(start + i) % pool.Keys.Length];
                if (skipKeyIds.Contains(key.Id)) continue;
                // A key with every place taken is tried again below, once one frees up.
                if (key.Slots is { } slots && !slots.Wait(0))
                {
                    busy ??= key;
                    continue;
                }
                if (await TryTakeAsync(pool, key, day, cancellationToken) is { } lease) return lease;
            }

            if (busy is null)
            {
                logger.LogWarning("Every system {Provider} key is used up or taken out of use for {Day}.", pool.Name, day);
                return null;
            }
            await busy.Slots!.WaitAsync(cancellationToken);
            if (await TryTakeAsync(pool, busy, day, cancellationToken) is { } waited) return waited;
        }
    }

    // Counts the use, holding the key's place already taken; the place is freed again when the key cannot be used.
    private async Task<ISystemApiKeyLease?> TryTakeAsync(ProviderKeys pool, PoolKey key, DateOnly day, CancellationToken cancellationToken)
    {
        bool taken;
        try
        {
            taken = await usage.TryTakeAsync(pool.Name, key.Id, day, pool.DailyLimit, cancellationToken);
        }
        catch
        {
            key.Slots?.Release();
            throw;
        }
        if (taken) return new Lease(this, pool.Name, key, day);
        key.Slots?.Release();
        return null;
    }

    // Whether the keys are taken in turn; an unknown strategy is named in the log and the keys are filled in order.
    private bool Rotates(string provider, string? strategy)
    {
        if (strategy is null || strategy.Equals(SystemApiKeyStrategies.Fill, StringComparison.OrdinalIgnoreCase)) return false;
        if (strategy.Equals(SystemApiKeyStrategies.Rotate, StringComparison.OrdinalIgnoreCase)) return true;
        logger.LogWarning("Unknown system {Provider} key strategy '{Strategy}': the keys are used in order. Use 'fill' or 'rotate'.",
            provider, strategy);
        return false;
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    // The first 12 hex digits of the key's SHA-256: enough to tell a handful of keys apart, never enough to find the key.
    internal static string KeyIdOf(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant()[..12];

    private sealed record PoolKey(string Id, string Value, SemaphoreSlim? Slots);

    private sealed class ProviderKeys(string name, PoolKey[] keys, int? dailyLimit, bool rotate)
    {
        public bool Rotate { get; } = rotate;
        public string Name { get; } = name;
        public PoolKey[] Keys { get; } = keys;
        public int? DailyLimit { get; } = dailyLimit;
        public int Next = -1;
    }

    private sealed class Lease(SystemApiKeyPool pool, string provider, PoolKey key, DateOnly day) : ISystemApiKeyLease
    {
        private int givenBack;
        private int released;

        public string Provider => provider;
        public string KeyId => key.Id;
        public string ApiKey => key.Value;

        public async Task RefundAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref givenBack, 1) == 0)
                await pool.usage.GiveBackAsync(provider, key.Id, day, cancellationToken);
        }

        public async Task DisableForTodayAsync(string reason, CancellationToken cancellationToken = default)
        {
            pool.logger.LogWarning("System {Provider} key {KeyId} is taken out of use for {Day}: {Reason}", provider, key.Id, day, reason);
            await pool.usage.DisableAsync(provider, key.Id, day, reason, cancellationToken);
            await RefundAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) key.Slots?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
