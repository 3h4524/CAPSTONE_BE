namespace APCS.Application.Features.Admin.SystemApiKeys.Dtos.Response;

/// <summary>Every provider the platform keeps keys for, with its settings and keys.</summary>
/// <param name="Providers">The providers, Ideogram first.</param>
/// <param name="StorageReady">
/// False while the database has no key tables yet (the migration has not reached it): there are no platform keys then.
/// </param>
public sealed record AdminSystemApiKeysDto(IReadOnlyList<AdminSystemApiKeyProviderDto> Providers, bool StorageReady);

/// <summary>One provider's settings and keys.</summary>
/// <param name="Provider">The provider, as stored: <c>"ideogram"</c>, <c>"minimax"</c>.</param>
/// <param name="Name">The provider's name.</param>
/// <param name="DailyLimitPerKey">How many requests each key may make in a day (UTC); null for no limit.</param>
/// <param name="MaxConcurrentPerKey">How many requests each key may have running at once on a server; null for no limit.</param>
/// <param name="Strategy"><c>"fill"</c> or <c>"rotate"</c>.</param>
/// <param name="UpdatedAt">When the settings were last changed; sent back with a change.</param>
/// <param name="Keys">The keys, in the order fill takes them.</param>
public sealed record AdminSystemApiKeyProviderDto(
    string Provider,
    string Name,
    int? DailyLimitPerKey,
    int? MaxConcurrentPerKey,
    string Strategy,
    DateTime UpdatedAt,
    IReadOnlyList<AdminSystemApiKeyDto> Keys);

/// <summary>One platform key, without the key itself.</summary>
/// <param name="Id">The key's row.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Label">The Admin's name for the key.</param>
/// <param name="MaskedKey">The key's last four characters, after asterisks.</param>
/// <param name="KeyId">The short id the key's use is counted and logged under.</param>
/// <param name="IsActive">Whether new requests use the key.</param>
/// <param name="UsedToday">Requests counted on the key today (UTC).</param>
/// <param name="RefusedToday">Why the provider refused the key today, or null while it is in use.</param>
/// <param name="Readable">Whether this server can decrypt the key; one it cannot is not used.</param>
/// <param name="LastCheckedAt">When the key was last checked with the provider.</param>
/// <param name="LastCheckSucceeded">Whether the provider accepted the key then.</param>
/// <param name="CreatedAt">When the key was added.</param>
/// <param name="UpdatedAt">When the key was last changed; sent back with a change.</param>
public sealed record AdminSystemApiKeyDto(
    Guid Id,
    string Provider,
    string Label,
    string MaskedKey,
    string KeyId,
    bool IsActive,
    int UsedToday,
    string? RefusedToday,
    bool Readable,
    DateTime? LastCheckedAt,
    bool? LastCheckSucceeded,
    DateTime CreatedAt,
    DateTime UpdatedAt);
