using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using APCS.Application.Abstractions.AI;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Hands out the platform's keys, by the provider's strategy: every request starts at the first key
/// (<see cref="SystemApiKeyStrategies.Fill"/>, the default), or at the key after the one the last request started at
/// (<see cref="SystemApiKeyStrategies.Rotate"/>). From there a key used up or refused for the day, or with every place
/// taken, is passed over for the next one. The daily counts live in <see cref="ISystemApiKeyUsageStore"/> (shared by every
/// server), the requests at a time on this server only.
/// </summary>
/// <remarks>
/// The keys and limits come from <see cref="ISystemApiKeySource"/>, read again by <see cref="SystemApiKeyPoolRefresher"/>
/// every minute and straight after <see cref="Invalidate"/>. Each read replaces the whole set at once; the places of a key
/// outlive the reads (while its limit of requests at a time stays the same), so requests already running still count.
/// </remarks>
public sealed class SystemApiKeyPool : ISystemApiKeyPool
{
    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>();

    private readonly ISystemApiKeySource source;
    private readonly ISystemApiKeyUsageStore usage;
    private readonly TimeProvider time;
    private readonly ILogger<SystemApiKeyPool> logger;
    // The places of each key ("provider:keyId"), kept across reads.
    private readonly ConcurrentDictionary<string, Slots> slots = new(StringComparer.Ordinal);
    // Where rotation is at for each provider, kept across reads.
    private readonly ConcurrentDictionary<string, StrongBox<int>> turns = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly SemaphoreSlim changed = new(0, 1);
    private volatile Snapshot? current;

    public SystemApiKeyPool(ISystemApiKeySource source, ISystemApiKeyUsageStore usage, TimeProvider time, ILogger<SystemApiKeyPool> logger)
    {
        this.source = source;
        this.usage = usage;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>Whether the keys were read at least once.</summary>
    public bool IsLoaded => current is not null;

    /// <summary>
    /// Reads the keys and limits again. A read that fails keeps the keys of the last one, and is reported to the caller.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await loading.WaitAsync(cancellationToken);
        try
        {
            var configs = await source.LoadAsync(cancellationToken);
            current = Build(configs);
        }
        finally
        {
            loading.Release();
        }
    }

    public void Invalidate()
    {
        try
        {
            changed.Release();
        }
        catch (SemaphoreFullException)
        {
            // A read is already asked for.
        }
    }

    /// <summary>Waits until <see cref="Invalidate"/> is called, or the time is up. True when it was called.</summary>
    internal Task<bool> WaitForChangeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        changed.WaitAsync(timeout, cancellationToken);

    public bool HasKeys(string provider) => Find(provider) is { Active.Length: > 0 };

    public string? FindKey(string provider, string keyId) =>
        Find(provider)?.All.FirstOrDefault(key => key.Id == keyId)?.Value;

    public string? AnyKey(string provider) => Find(provider) is { Active.Length: > 0 } keys ? keys.Active[0].Value : null;

    public Task<ISystemApiKeyLease?> AcquireAsync(string provider, CancellationToken cancellationToken = default) =>
        AcquireAsync(provider, NoKeys, cancellationToken);

    public async Task<ISystemApiKeyLease?> AcquireAsync(string provider, IReadOnlySet<string> skipKeyIds,
        CancellationToken cancellationToken = default)
    {
        if (current is null) await RefreshOnceAsync(cancellationToken);

        while (true)
        {
            // Each try reads the keys as they are now: an Admin may have switched one off since the last.
            if (Find(provider) is not { Active.Length: > 0 } pool) return null;
            var keys = pool.Active;
            var day = Today();
            var start = pool.Rotate ? (int)((uint)Interlocked.Increment(ref pool.Turn.Value) % (uint)keys.Length) : 0;
            PoolKey? busy = null;
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[(start + i) % keys.Length];
                if (skipKeyIds.Contains(key.Id)) continue;
                // A key with every place taken is tried again below, once one frees up.
                if (key.Slots is { } places && !places.Semaphore.Wait(0))
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
            await busy.Slots!.Semaphore.WaitAsync(cancellationToken);
            if (await TryTakeAsync(pool, busy, day, cancellationToken) is { } waited) return waited;
        }
    }

    public Task<IReadOnlyDictionary<string, SystemApiKeyUsageToday>> GetUsageTodayAsync(string provider,
        IReadOnlyCollection<string> keyIds, CancellationToken cancellationToken = default) =>
        usage.GetAsync(provider.ToLowerInvariant(), keyIds, Today(), cancellationToken);

    public Task ClearRefusedTodayAsync(string provider, string keyId, CancellationToken cancellationToken = default) =>
        usage.ClearDisabledAsync(provider.ToLowerInvariant(), keyId, Today(), cancellationToken);

    // Reads the keys the first time they are needed, when the refresher has not read them yet (a server just starting).
    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (current is null) await RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The system API keys could not be read");
        }
    }

    private ProviderKeys? Find(string provider) =>
        current is { } snapshot && snapshot.Providers.TryGetValue(provider, out var keys) ? keys : null;

    private Snapshot Build(IReadOnlyList<SystemApiKeyProviderConfig> configs)
    {
        var providers = new Dictionary<string, ProviderKeys>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var name = config.Provider.ToLowerInvariant();
            var all = config.Keys
                .Where(key => !string.IsNullOrWhiteSpace(key.Value))
                .DistinctBy(key => key.Value.Trim(), StringComparer.Ordinal)
                .Select(key =>
                {
                    var id = KeyIdOf(key.Value);
                    return new PoolKey(id, key.Value.Trim(), key.IsActive, SlotsFor(name, id, config.MaxConcurrentPerKey));
                })
                .ToArray();
            providers[name] = new ProviderKeys(name, all, all.Where(key => key.IsActive).ToArray(), config.DailyLimitPerKey,
                Rotates(name, config.Strategy), turns.GetOrAdd(name, _ => new StrongBox<int>(-1)));
        }
        return new Snapshot(providers);
    }

    // The key's places: the same ones as before the read, unless how many there are has changed. Requests holding a place
    // of the old ones give it back to those.
    private Slots? SlotsFor(string provider, string keyId, int? maxConcurrent)
    {
        var name = $"{provider}:{keyId}";
        if (maxConcurrent is not (int capacity and > 0))
        {
            slots.TryRemove(name, out _);
            return null;
        }
        return slots.AddOrUpdate(name,
            _ => new Slots(capacity, new SemaphoreSlim(capacity, capacity)),
            (_, existing) => existing.Capacity == capacity ? existing : new Slots(capacity, new SemaphoreSlim(capacity, capacity)));
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
            key.Slots?.Semaphore.Release();
            throw;
        }
        if (taken) return new Lease(this, pool.Name, key, day);
        key.Slots?.Semaphore.Release();
        return null;
    }

    // Whether the keys are taken in turn; an unknown strategy is named in the log and the keys are filled in order.
    private bool Rotates(string provider, string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy) || strategy.Equals(SystemApiKeyStrategies.Fill, StringComparison.OrdinalIgnoreCase))
            return false;
        if (strategy.Equals(SystemApiKeyStrategies.Rotate, StringComparison.OrdinalIgnoreCase)) return true;
        logger.LogWarning("Unknown system {Provider} key strategy '{Strategy}': the keys are used in order. Use 'fill' or 'rotate'.",
            provider, strategy);
        return false;
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>The short id of the key: see <see cref="SystemApiKeyIds.Of"/>.</summary>
    public static string KeyIdOf(string key) => SystemApiKeyIds.Of(key);

    private sealed record Snapshot(IReadOnlyDictionary<string, ProviderKeys> Providers);

    private sealed record Slots(int Capacity, SemaphoreSlim Semaphore);

    private sealed record PoolKey(string Id, string Value, bool IsActive, Slots? Slots);

    private sealed record ProviderKeys(string Name, PoolKey[] All, PoolKey[] Active, int? DailyLimit, bool Rotate, StrongBox<int> Turn);

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
            if (Interlocked.Exchange(ref released, 1) == 0) key.Slots?.Semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Reads the platform keys before the server takes requests, then every <see cref="Interval"/> (an edit made through
/// another server) and straight after an Admin's edit through this one.
/// </summary>
public sealed class SystemApiKeyPoolRefresher(SystemApiKeyPool pool, ILogger<SystemApiKeyPoolRefresher> logger)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    /// <summary>How long an edit made through another server takes to reach this one.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await pool.WaitForChangeAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await pool.RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The system API keys could not be read; the keys read last stay in use");
        }
    }
}
