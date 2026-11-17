using APCS.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace APCS.Infrastructure.UnitTests.TestSupport;

/// <summary>The platform keys for tests, as an Admin would have saved them; change them and refresh the pool.</summary>
internal sealed class FakeSystemApiKeySource : ISystemApiKeySource
{
    /// <summary>The providers the next read returns.</summary>
    public List<SystemApiKeyProviderConfig> Providers { get; } = [];

    /// <summary>When set, the next read fails with it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>How many times the keys were read.</summary>
    public int Loads { get; private set; }

    public Task<IReadOnlyList<SystemApiKeyProviderConfig>> LoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<SystemApiKeyProviderConfig>>(Providers.ToList());
    }

    /// <summary>Replaces the provider's keys and limits.</summary>
    public FakeSystemApiKeySource Set(SystemApiKeyProviderConfig provider)
    {
        Providers.RemoveAll(existing => existing.Provider == provider.Provider);
        Providers.Add(provider);
        return this;
    }
}

internal static class SystemApiKeyTestPools
{
    /// <summary>One provider's keys, all switched on.</summary>
    public static SystemApiKeyProviderConfig Provider(string provider, IEnumerable<string> keys, int? dailyLimit = null,
        int? concurrency = null, string? strategy = null) =>
        new(provider, dailyLimit, concurrency, strategy, keys.Select(key => new SystemApiKeyConfig(key, true)).ToList());

    /// <summary>A pool that has read the source once.</summary>
    public static SystemApiKeyPool Create(FakeSystemApiKeySource source, ISystemApiKeyUsageStore? store = null, TimeProvider? time = null)
    {
        var pool = new SystemApiKeyPool(source, store ?? new InMemorySystemApiKeyUsageStore(), time ?? TimeProvider.System,
            NullLogger<SystemApiKeyPool>.Instance);
        pool.RefreshAsync().GetAwaiter().GetResult();
        return pool;
    }

    /// <summary>A pool with these providers' keys.</summary>
    public static SystemApiKeyPool Create(IEnumerable<SystemApiKeyProviderConfig> providers, ISystemApiKeyUsageStore? store = null,
        TimeProvider? time = null)
    {
        var source = new FakeSystemApiKeySource();
        foreach (var provider in providers) source.Set(provider);
        return Create(source, store, time);
    }
}
