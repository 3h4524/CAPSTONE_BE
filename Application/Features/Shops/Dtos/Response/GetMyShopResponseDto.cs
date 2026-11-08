namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>The seller's Etsy connection and, when Etsy answers, their shop.</summary>
/// <param name="Status">
/// <c>not_connected</c>, <c>connected</c>, <c>reconnect_required</c> (the grant expired or was revoked) or
/// <c>unavailable</c> (connected, but Etsy could not be reached; the stored shop name is still returned).
/// </param>
/// <param name="EtsyConfigured">False when the server has no Etsy app key, so connecting cannot start.</param>
public sealed record GetMyShopResponseDto(
    string Status,
    bool EtsyConfigured,
    ShopDetailsDto? Shop,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastSyncAtUtc,
    DateTimeOffset? ReconnectByUtc);

/// <summary>The Etsy shop figures shown to the seller.</summary>
public sealed record ShopDetailsDto(
    string ShopId,
    string ShopName,
    string? Title,
    string? Url,
    string? IconUrl,
    string? CurrencyCode,
    int? ActiveListingCount,
    int? DigitalListingCount,
    int? FavorerCount,
    int? SoldCount,
    decimal? ReviewAverage,
    int? ReviewCount,
    bool? IsOnVacation,
    DateTimeOffset? OpenedAtUtc);
