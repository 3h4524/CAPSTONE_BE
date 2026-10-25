namespace APCS.Application.Abstractions.Commerce;

/// <summary>
/// The minimum connection context a connector needs for one connected shop.
/// Access tokens are runtime values and must not be persisted in this contract.
/// </summary>
public sealed record CommerceConnection(
    Guid ConnectionId,
    Guid OwnerUserId,
    CommercePlatform Platform,
    string ExternalStoreId,
    string AccessToken);

/// <summary>
/// Platform-neutral listing data used by application services and insight jobs.
/// </summary>
public sealed record CommerceListing(
    string ExternalListingId,
    string ExternalStoreId,
    string Title,
    string? Url,
    decimal? Price,
    string? Currency,
    int? Quantity,
    int? Favorites,
    DateTimeOffset? CreatedAtUtc,
    IReadOnlyCollection<string> Tags);

/// <summary>
/// Platform-neutral performance data for a listing within a time window.
/// Unsupported platform metrics remain null instead of being fabricated.
/// </summary>
public sealed record CommercePerformanceSnapshot(
    string ExternalListingId,
    string ExternalStoreId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int? Orders,
    int? UnitsSold,
    decimal? Revenue,
    int? Favorites,
    int? Views,
    int? Visits,
    decimal? ConversionRate,
    string SourcePlatform);

/// <summary>Defines a public marketplace research query.</summary>
public sealed record CommerceMarketSearchRequest(
    IReadOnlyCollection<string> Queries,
    int LimitPerQuery = 25,
    string? Language = null);

/// <summary>Normalized public listing data used for cross-platform market research.</summary>
public sealed record CommerceMarketListing(
    CommercePlatform Platform,
    string ExternalListingId,
    string? ExternalSellerId,
    string Title,
    string? Description,
    string? Url,
    decimal? Price,
    string? Currency,
    int? Quantity,
    int? Favorites,
    int? Views,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyCollection<string> Tags,
    string? MatchedQuery = null);
