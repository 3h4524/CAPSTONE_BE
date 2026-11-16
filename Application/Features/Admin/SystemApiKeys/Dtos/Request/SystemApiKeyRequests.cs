namespace APCS.Application.Features.Admin.SystemApiKeys.Dtos.Request;

/// <summary>A platform key to add. It is checked with the provider before it is saved.</summary>
/// <param name="Provider">The provider: <c>"ideogram"</c> or <c>"minimax"</c>.</param>
/// <param name="Label">A name for the key that tells Admins which account it is.</param>
/// <param name="Key">The key itself; never sent back.</param>
public sealed record AddSystemApiKeyRequestDto(string Provider, string Label, string Key);

/// <summary>A change to a platform key. The key itself never changes: a new key is added and the old one deleted.</summary>
/// <param name="Label">A name for the key that tells Admins which account it is.</param>
/// <param name="IsActive">
/// Whether new requests use the key. A video a switched-off key started is still finished with it.
/// </param>
/// <param name="UpdatedAt">When the key was last changed, as read: a change made since by another Admin is not overwritten.</param>
public sealed record UpdateSystemApiKeyRequestDto(string Label, bool IsActive, DateTime UpdatedAt);

/// <summary>How far each of a provider's platform keys is used.</summary>
/// <param name="DailyLimitPerKey">How many requests each key may make in a day (UTC); null for no limit.</param>
/// <param name="MaxConcurrentPerKey">How many requests each key may have running at once on a server; null for no limit.</param>
/// <param name="Strategy"><c>"fill"</c> (one key until it is used up) or <c>"rotate"</c> (the keys in turn).</param>
/// <param name="UpdatedAt">When the settings were last changed, as read: a change made since by another Admin is not overwritten.</param>
public sealed record UpdateSystemApiKeyProviderRequestDto(int? DailyLimitPerKey, int? MaxConcurrentPerKey, string Strategy, DateTime UpdatedAt);

/// <summary>The order fill takes a provider's keys in.</summary>
/// <param name="KeyIds">Every key of the provider, each once, first to last.</param>
public sealed record ReorderSystemApiKeysRequestDto(IReadOnlyList<Guid> KeyIds);
