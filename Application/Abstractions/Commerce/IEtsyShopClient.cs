namespace APCS.Application.Abstractions.Commerce;

/// <summary>Tokens issued by Etsy for one connected seller.</summary>
/// <param name="AccessToken">Bearer token for the seller's data; lives about an hour.</param>
/// <param name="RefreshToken">Exchanged for a new access token; replaced on every refresh.</param>
/// <param name="AccessTokenExpiresAtUtc">When <paramref name="AccessToken"/> stops working.</param>
public sealed record EtsyTokenSet(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAtUtc);

/// <summary>A seller's Etsy shop as Etsy reports it.</summary>
public sealed record EtsyShopDetails(
    string ShopId,
    string ShopName,
    string? Title,
    string? Url,
    string? IconUrl,
    string? CurrencyCode,
    int ActiveListingCount,
    int DigitalListingCount,
    int FavorerCount,
    int SoldCount,
    decimal? ReviewAverage,
    int ReviewCount,
    bool IsOnVacation,
    DateTimeOffset? OpenedAtUtc,
    string? CoverImageUrl = null,
    string? Announcement = null,
    bool HasReturnPolicy = false,
    bool IsEtsyPaymentsOnboarded = false,
    bool AcceptsCustomRequests = false,
    string? ShipsFromCountry = null);

/// <summary>An amount of money; Etsy sends it as an integer over a divisor.</summary>
public sealed record EtsyMoney(decimal Amount, string Currency);

/// <summary>One listing of the seller's shop.</summary>
/// <param name="Views">Etsy tallies views once a day and only for active listings.</param>
public sealed record EtsyListing(
    string ListingId,
    string Title,
    string? Url,
    string State,
    EtsyMoney? Price,
    int Quantity,
    int Views,
    int Favorers,
    IReadOnlyList<string> Tags,
    int ImageCount,
    string? ImageUrl,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? EndingAtUtc);

/// <summary>A page of listings with Etsy's total for the query.</summary>
public sealed record EtsyListingPage(int Count, IReadOnlyList<EtsyListing> Listings);

/// <summary>One order of the seller's shop.</summary>
public sealed record EtsyReceipt(
    string ReceiptId,
    string Status,
    bool IsPaid,
    bool IsShipped,
    DateTimeOffset CreatedAtUtc,
    string? CountryIso,
    EtsyMoney? GrandTotal,
    EtsyMoney? Refunded,
    IReadOnlyList<EtsyReceiptItem> Items);

/// <summary>One line of an order.</summary>
public sealed record EtsyReceiptItem(
    string? ListingId,
    string Title,
    int Quantity,
    EtsyMoney? Price,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset? ShippedAtUtc,
    DateTimeOffset? ExpectedShipAtUtc);

/// <summary>What the buyer paid for an order, what Etsy kept and what the seller received.</summary>
public sealed record EtsyPayment(EtsyMoney? Gross, EtsyMoney? Fees, EtsyMoney? Net);

/// <summary>A buyer review of the shop.</summary>
public sealed record EtsyReview(int Rating, string? Text, string? ListingId, DateTimeOffset? CreatedAtUtc);

/// <summary>Shop settings a listing needs before it can be created.</summary>
public sealed record EtsyShopSetup(int ShippingProfileCount, int ProductionPartnerCount, int SectionCount);

/// <summary>A choice from the seller's shop settings (shipping profile, processing profile, production partner).</summary>
public sealed record EtsyOption(long Id, string Label);

/// <summary>One category of Etsy's seller taxonomy, with its full path ("Clothing > Shirts > T-shirts").</summary>
public sealed record EtsyTaxonomyNode(long Id, string Path);

/// <summary>The fields of a new draft listing.</summary>
/// <param name="Type"><c>physical</c> or <c>download</c>.</param>
/// <param name="ShippingProfileId">Required by Etsy for physical listings.</param>
public sealed record EtsyDraftListing(
    string Title,
    string Description,
    decimal Price,
    int Quantity,
    long TaxonomyId,
    string Type,
    string WhoMade,
    string WhenMade,
    IReadOnlyList<string> Tags,
    long? ShippingProfileId,
    long? ReadinessStateId,
    IReadOnlyList<long> ProductionPartnerIds);

/// <summary>A listing Etsy created.</summary>
public sealed record EtsyCreatedListing(string ListingId, string? Url);

/// <summary>Why an Etsy call failed, as far as a use case needs to react.</summary>
public enum EtsyFailureKind
{
    /// <summary>The seller's grant is no longer valid: they must connect again.</summary>
    Unauthorized,
    /// <summary>Etsy limited the request rate; retry later.</summary>
    RateLimited,
    /// <summary>Etsy could not be reached or answered with an error.</summary>
    Unavailable,
}

/// <summary>An Etsy call failed; carries no token or response body.</summary>
public sealed class EtsyApiException(EtsyFailureKind kind, string message, string? reason = null) : Exception(message)
{
    public EtsyFailureKind Kind { get; } = kind;

    /// <summary>Etsy's explanation of a refused request (its `error` text), safe to show the seller.</summary>
    public string? Reason { get; } = reason;
}

/// <summary>
/// Connects a seller's Etsy account through OAuth 2.0 with PKCE and reads their shop.
/// Etsy-specific HTTP, scopes and token storage format stay in the Infrastructure adapter.
/// </summary>
public interface IEtsyShopClient
{
    /// <summary>Whether an Etsy app key is configured; without it no seller can connect.</summary>
    bool IsConfigured { get; }

    /// <summary>The Etsy consent page the seller is sent to.</summary>
    string BuildAuthorizationUrl(string state, string codeChallenge);

    /// <summary>The client page the seller returns to after the consent page, with the outcome.</summary>
    string BuildReturnUrl(string outcome);

    Task<EtsyTokenSet> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    Task<EtsyTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>The shop of the token's owner, or null when the Etsy account has no shop.</summary>
    Task<string?> GetOwnShopIdAsync(string accessToken, CancellationToken cancellationToken);

    Task<EtsyShopDetails> GetShopAsync(string accessToken, string shopId, CancellationToken cancellationToken);

    /// <summary>Listings in one state, most recently updated first; <c>Count</c> is Etsy's total for that state.</summary>
    Task<EtsyListingPage> GetListingsAsync(string accessToken, string shopId, string state, int limit, bool withImages, CancellationToken cancellationToken);

    /// <summary>Orders created in the window, newest first, up to <paramref name="maxReceipts"/>.</summary>
    Task<IReadOnlyList<EtsyReceipt>> GetReceiptsAsync(string accessToken, string shopId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxReceipts, CancellationToken cancellationToken);

    /// <summary>The payment of one order, or null when Etsy has none for it.</summary>
    Task<EtsyPayment?> GetReceiptPaymentAsync(string accessToken, string shopId, string receiptId, CancellationToken cancellationToken);

    /// <summary>The newest shop reviews, up to <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<EtsyReview>> GetReviewsAsync(string accessToken, string shopId, int limit, CancellationToken cancellationToken);

    Task<EtsyShopSetup> GetShopSetupAsync(string accessToken, string shopId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EtsyOption>> GetShippingProfilesAsync(string accessToken, string shopId, CancellationToken cancellationToken);

    /// <summary>Processing profiles (Etsy's readiness states): ready to ship or made to order, with processing days.</summary>
    Task<IReadOnlyList<EtsyOption>> GetProcessingProfilesAsync(string accessToken, string shopId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EtsyOption>> GetProductionPartnersAsync(string accessToken, string shopId, CancellationToken cancellationToken);

    /// <summary>Every leaf and branch of Etsy's seller taxonomy, flattened.</summary>
    Task<IReadOnlyList<EtsyTaxonomyNode>> GetSellerTaxonomyAsync(CancellationToken cancellationToken);

    /// <summary>Creates a listing in the <c>draft</c> state; nothing is published or charged.</summary>
    Task<EtsyCreatedListing> CreateDraftListingAsync(string accessToken, string shopId, EtsyDraftListing listing, CancellationToken cancellationToken);

    Task UploadListingImageAsync(string accessToken, string shopId, string listingId, byte[] image, string fileName, int rank, string? altText, CancellationToken cancellationToken);

    /// <summary>Downloads the video at <paramref name="sourceUrl"/> and attaches it to the listing.</summary>
    Task UploadListingVideoAsync(string accessToken, string shopId, string listingId, string sourceUrl, string name, CancellationToken cancellationToken);

    /// <summary>Attaches a file the buyer downloads after paying, for a digital listing. Etsy allows five, 20 MB each.</summary>
    Task UploadListingFileAsync(string accessToken, string shopId, string listingId, string sourceUrl, string fileName, int rank, CancellationToken cancellationToken);

    /// <summary>Encrypts a token for storage.</summary>
    string Protect(string value);

    /// <summary>Decrypts a stored token.</summary>
    string Unprotect(string value);
}
