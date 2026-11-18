namespace APCS.Application.Abstractions.AI;

/// <summary>The providers the platform keeps keys of its own for, named as the keys are stored.</summary>
public static class SystemApiKeyProviders
{
    /// <summary>Ideogram, for images.</summary>
    public const string Ideogram = "ideogram";

    /// <summary>MiniMax, for AI video shots.</summary>
    public const string MiniMax = "minimax";

    /// <summary>Every provider, in the order an Admin sees them.</summary>
    public static readonly IReadOnlyList<string> All = [Ideogram, MiniMax];

    /// <summary>The name an Admin reads.</summary>
    public static string DisplayName(string provider) => provider switch
    {
        Ideogram => "Ideogram",
        MiniMax => "MiniMax",
        _ => provider,
    };
}

/// <summary>How a provider's platform keys are taken.</summary>
public static class SystemApiKeyStrategies
{
    /// <summary>
    /// The first key until it is used up or refused for the day, then the next one. A request finding every place of a key
    /// taken goes on to the next key rather than waiting, so a busy hour still runs on every key.
    /// </summary>
    public const string Fill = "fill";

    /// <summary>Each request starts at the key after the one the last request started at, so the uses spread evenly.</summary>
    public const string Rotate = "rotate";
}

/// <summary>
/// Encrypts the platform's keys for the database, with a key ring every server on the same database shares: a key saved
/// through one server is read by all of them.
/// </summary>
public interface ISystemApiKeySecrets
{
    /// <summary>The key, encrypted.</summary>
    string Protect(string value);

    /// <summary>
    /// The key back from <see cref="Protect"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// when the key ring no longer holds what encrypted it.
    /// </summary>
    string Unprotect(string value);
}

/// <summary>How far a platform key has been used today (UTC).</summary>
/// <param name="Used">The uses counted today.</param>
/// <param name="RefusedReason">Why the provider refused the key today, or null while it is in use.</param>
public sealed record SystemApiKeyUsageToday(int Used, string? RefusedReason);

/// <summary>The short id a platform key is known by, in the database, in Redis and in the logs.</summary>
public static class SystemApiKeyIds
{
    /// <summary>
    /// The first 12 hex digits of the key's SHA-256: enough to tell a handful of keys apart, never enough to find the key.
    /// </summary>
    public static string Of(string key) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.Trim())))
            .ToLowerInvariant()[..12];
}
