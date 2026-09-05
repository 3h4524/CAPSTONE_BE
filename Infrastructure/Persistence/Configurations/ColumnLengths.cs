namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provides the shared column length budget used across entity configurations.
/// </summary>
/// <remarks>
/// Naming the sizes keeps equivalent columns in step: a deviation has to be written as a literal,
/// which makes it visible in review instead of drifting silently.
/// </remarks>
internal static class ColumnLengths
{
    /// <summary>Short enum-like discriminators: statuses, types, providers, platforms.</summary>
    public const int Code = 50;

    /// <summary>Longer codes and model identifiers.</summary>
    public const int LongCode = 100;

    /// <summary>Display names, titles, and external identifiers.</summary>
    public const int Name = 255;

    /// <summary>Object-storage keys and archive paths.</summary>
    public const int StorageKey = 500;

    /// <summary>Email addresses.</summary>
    public const int Email = 255;

    /// <summary>Hashed token values.</summary>
    public const int TokenHash = 255;

    /// <summary>An IPv4 or IPv6 address in text form.</summary>
    public const int IpAddress = 45;

    /// <summary>Identity security and concurrency stamps.</summary>
    public const int Stamp = 64;

    /// <summary>Marketplace listing titles.</summary>
    public const int ListingTitle = 140;

    /// <summary>Marketplace tag values.</summary>
    public const int Tag = 20;
}
