using APCS.Application.Abstractions.AI;

namespace APCS.Application.UnitTests.TestSupport;

/// <summary>
/// The platform's keys for tests: each key has a number of uses left today, handed out in order. Records the keys
/// taken out of use for the day.
/// </summary>
internal sealed class FakeSystemApiKeyPool(string provider, params (string Key, int Uses)[] keys) : ISystemApiKeyPool
{
    private readonly Dictionary<string, int> left = keys.ToDictionary(key => key.Key, key => key.Uses);

    /// <summary>The keys taken out of use for today, with the reason given.</summary>
    public List<(string Key, string Reason)> Disabled { get; } = [];

    public bool HasKeys(string name) => string.Equals(name, provider, StringComparison.OrdinalIgnoreCase) && left.Count > 0;

    public Task<ISystemApiKeyLease?> AcquireAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasKeys(name)) return Task.FromResult<ISystemApiKeyLease?>(null);
        var key = left.FirstOrDefault(pair => pair.Value > 0 && Disabled.All(off => off.Key != pair.Key)).Key;
        if (key is null) return Task.FromResult<ISystemApiKeyLease?>(null);
        left[key]--;
        return Task.FromResult<ISystemApiKeyLease?>(new Lease(this, name, key));
    }

    public string? FindKey(string name, string keyId) => HasKeys(name) && left.ContainsKey(keyId) ? keyId : null;

    public string? AnyKey(string name) => HasKeys(name) ? left.Keys.First() : null;

    private sealed class Lease(FakeSystemApiKeyPool pool, string provider, string key) : ISystemApiKeyLease
    {
        public string Provider => provider;
        public string KeyId => key;
        public string ApiKey => key;

        public Task RefundAsync(CancellationToken cancellationToken = default)
        {
            pool.left[key]++;
            return Task.CompletedTask;
        }

        public Task DisableForTodayAsync(string reason, CancellationToken cancellationToken = default)
        {
            pool.Disabled.Add((key, reason));
            return RefundAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
