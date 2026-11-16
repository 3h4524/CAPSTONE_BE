namespace APCS.Application.Abstractions.AI;

/// <summary>
/// The platform's own API keys for an AI provider (Ideogram images, MiniMax video), used in turn. Each key is used up to
/// its own daily limit and, where set, by a few requests at a time; once a key is used up or refused for the day the
/// next one is taken.
/// </summary>
/// <remarks>
/// A provider is named the way its keys are stored: <c>"ideogram"</c>, <c>"minimax"</c>. The keys are the ones an Admin
/// keeps on the admin page (the <c>system_api_keys</c> table), never a Seller's own connections. They are read again a
/// minute after the last read, or at once after <see cref="Invalidate"/>.
/// </remarks>
public interface ISystemApiKeyPool
{
    /// <summary>Whether the platform has any key for the provider at all.</summary>
    bool HasKeys(string provider);

    /// <summary>
    /// Takes one use of a key with some of today's limit left, waiting while every such key is at its limit of
    /// requests at a time. Null when the provider has no key, or every key is used up or refused for today.
    /// </summary>
    Task<ISystemApiKeyLease?> AcquireAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="AcquireAsync(string, CancellationToken)"/>, passing over the keys whose
    /// <see cref="ISystemApiKeyLease.KeyId"/> is in <paramref name="skipKeyIds"/>: keys this request already found busy.
    /// </summary>
    Task<ISystemApiKeyLease?> AcquireAsync(string provider, IReadOnlySet<string> skipKeyIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The key a lease handed out, found again by <see cref="ISystemApiKeyLease.KeyId"/>: a task a key started (a video
    /// being made) is asked about with that same key. Null when the key is no longer configured.
    /// </summary>
    string? FindKey(string provider, string keyId);

    /// <summary>Any one of the provider's keys, to check that the platform can serve it; null when it has none.</summary>
    string? AnyKey(string provider);

    /// <summary>Reads the keys and limits again soon: an Admin changed them on this server.</summary>
    void Invalidate();

    /// <summary>How far each of the keys has been used today, by <see cref="ISystemApiKeyLease.KeyId"/>.</summary>
    Task<IReadOnlyDictionary<string, SystemApiKeyUsageToday>> GetUsageTodayAsync(string provider, IReadOnlyCollection<string> keyIds,
        CancellationToken cancellationToken = default);

    /// <summary>Puts a key the provider refused today back in use: an Admin fixed the account (added credits, say).</summary>
    Task ClearRefusedTodayAsync(string provider, string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One use of a platform key, counted against its daily limit when it was taken. Disposing it frees the key's place
/// for the next request at the same time; the use itself stays counted unless it is given back.
/// </summary>
public interface ISystemApiKeyLease : IAsyncDisposable
{
    /// <summary>The provider the key is for.</summary>
    string Provider { get; }

    /// <summary>A short id of the key that does not reveal it, to find it again and to name it in logs.</summary>
    string KeyId { get; }

    /// <summary>The key itself, to send to the provider.</summary>
    string ApiKey { get; }

    /// <summary>Gives the use back: the provider made and charged nothing for it.</summary>
    Task RefundAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the key out of use until the end of the day (UTC) and gives the use back: the provider refused the account
    /// (no credits, a key it no longer accepts, its own daily limit).
    /// </summary>
    Task DisableForTodayAsync(string reason, CancellationToken cancellationToken = default);
}
