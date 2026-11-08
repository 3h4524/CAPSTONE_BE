namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>
/// Insights computed from the seller's Etsy shop. Each section is read separately: a section Etsy could not
/// answer is null and named in <see cref="UnavailableSections"/>, so the rest still shows.
/// </summary>
/// <param name="Status">The connection status, as in <see cref="GetMyShopResponseDto.Status"/>.</param>
/// <param name="Currency">The shop currency every amount is in; amounts in another currency are left out.</param>
public sealed record GetShopInsightsResponseDto(
    string Status,
    DateTimeOffset GeneratedAtUtc,
    string? Currency,
    IReadOnlyList<ShopHealthCheckDto> Health,
    ListingInsightsDto? Listings,
    OrderInsightsDto? Orders,
    ReviewInsightsDto? Reviews,
    IReadOnlyList<string> UnavailableSections);

/// <summary>One readiness check of the shop; <paramref name="Code"/> is stable for the client.</summary>
public sealed record ShopHealthCheckDto(string Code, bool Passed, string Title, string Detail);

/// <param name="CountsByState">Etsy's own totals for active, draft, inactive, sold_out and expired listings.</param>
/// <param name="Recent">The most recently updated active listings, with the issues found on each.</param>
public sealed record ListingInsightsDto(
    IReadOnlyDictionary<string, int> CountsByState,
    IReadOnlyList<ListingRowDto> Recent,
    ListingIssueSummaryDto Issues);

/// <param name="Issues">Codes: <c>low_stock</c>, <c>expiring</c>, <c>few_tags</c>, <c>few_photos</c>, <c>viewed_not_favorited</c>.</param>
public sealed record ListingRowDto(
    string ListingId,
    string Title,
    string? Url,
    string? ImageUrl,
    string State,
    decimal? Price,
    string? Currency,
    int Quantity,
    int Views,
    int Favorites,
    int TagCount,
    int ImageCount,
    IReadOnlyList<string> Tags,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? EndingAtUtc,
    IReadOnlyList<string> Issues);

/// <summary>How many of the <paramref name="Checked"/> listings have each issue.</summary>
public sealed record ListingIssueSummaryDto(int Checked, int LowStock, int Expiring, int FewTags, int FewPhotos, int ViewedNotFavorited);

/// <summary>
/// Orders of the last <paramref name="WindowDays"/> days against the window before. Canceled orders are left out
/// of revenue and counts.
/// </summary>
/// <param name="Fees">Etsy fees on the paid orders whose payment was read (<paramref name="FeeSampleOrders"/>).</param>
/// <param name="Truncated">True when the shop had more orders than were read, so totals are a lower bound.</param>
public sealed record OrderInsightsDto(
    int WindowDays,
    int Orders,
    int PreviousOrders,
    decimal Revenue,
    decimal PreviousRevenue,
    int ItemsSold,
    decimal? AverageOrderValue,
    IReadOnlyList<DailyOrdersDto> Daily,
    int AwaitingShipment,
    int LateShipments,
    double? AverageFulfillmentHours,
    int FulfillmentSampleSize,
    IReadOnlyList<TopListingDto> TopListings,
    IReadOnlyList<CountryShareDto> TopCountries,
    decimal Refunded,
    int RefundedOrders,
    decimal? Fees,
    decimal? Net,
    int FeeSampleOrders,
    bool Truncated);

public sealed record DailyOrdersDto(DateOnly Day, int Orders, decimal Revenue);

public sealed record TopListingDto(string? ListingId, string Title, int Units, decimal Revenue);

public sealed record CountryShareDto(string CountryIso, int Orders);

/// <param name="Distribution">Reviews per star, 1 to 5, over the sampled reviews.</param>
public sealed record ReviewInsightsDto(
    decimal? Average,
    int Sampled,
    IReadOnlyDictionary<int, int> Distribution,
    IReadOnlyList<ReviewDto> Recent);

public sealed record ReviewDto(int Rating, string? Text, string? ListingId, DateTimeOffset? CreatedAtUtc);
