namespace APCS.Application.Features.Shops.Dtos.Response;

/// <summary>Whether the seller's Printify key (managed on API Keys) works, and the store it publishes through.</summary>
/// <param name="Status"><c>connected</c>, <c>not_connected</c> (no key), <c>key_invalid</c>, <c>no_store</c> or <c>unavailable</c>.</param>
/// <param name="SalesChannel">Where the store publishes; <c>etsy</c> is the one that fulfils Etsy orders.</param>
/// <param name="KeyName">The key's name on the API Keys page.</param>
/// <param name="KeyCheckedAtUtc">When API Keys last checked the key.</param>
public sealed record PrintifyConnectionDto(
    string Status,
    long? ShopId,
    string? ShopTitle,
    string? SalesChannel,
    string? KeyName,
    DateTimeOffset? KeyCheckedAtUtc,
    int ProductsCreated);

public sealed record PrintifyBlueprintDto(long Id, string Title, string Brand, string Model, string? ImageUrl);

/// <param name="Country">Where the provider ships from; a US provider delivers to US buyers fastest.</param>
public sealed record PrintifyProviderDto(long Id, string Title, string? Country, string? Region);

/// <summary>What a provider makes of a blueprint: its variants, and its US shipping rate for one item and each extra.</summary>
public sealed record PrintifyCatalogDto(
    long BlueprintId,
    long ProviderId,
    IReadOnlyList<PrintifyVariantOptionDto> Variants,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Positions,
    decimal? UsShippingFirstItem,
    decimal? UsShippingAdditionalItem,
    IReadOnlyList<PrintifyPrintAreaDto>? PrintAreas = null);

/// <summary>A print area of the provider's product and its size in pixels, the largest any of its variants has.</summary>
public sealed record PrintifyPrintAreaDto(string Position, int Width, int Height);

public sealed record PrintifyVariantOptionDto(long Id, string Title, string? Color, string? Size);

/// <summary>The Printify push form for a product.</summary>
/// <param name="EtsyConnected">Whether APCS can add its mock-ups and video to the Etsy listing Printify creates.</param>
public sealed record PrintifyDraftResponseDto(
    Guid ProductId,
    string ProductName,
    PrintifyConnectionDto Connection,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ShopDesignDto> Designs,
    IReadOnlyList<EtsyDraftMockupDto> Mockups,
    EtsyDraftVideoDto? Video,
    PrintifyProductSettingsDto Defaults,
    IReadOnlyList<PrintifyHistoryDto> History,
    bool EtsyConnected);

/// <summary>The choices of the seller's last Printify push.</summary>
public sealed record PrintifyProductSettingsDto(
    long? BlueprintId,
    string? BlueprintTitle,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> Sizes,
    decimal Price);

/// <param name="State"><c>created</c> (on Printify only), <c>publishing</c>, <c>published</c> (on Etsy) or <c>failed</c>.</param>
public sealed record PrintifyHistoryDto(
    string? PrintifyProductId,
    string State,
    string? EtsyListingId,
    string? EtsyUrl,
    DateTimeOffset AttemptedAtUtc,
    string? Error);

/// <summary>A product on Printify, its prices against Printify's costs, and how far publishing to Etsy got.</summary>
/// <param name="State"><c>created</c>, <c>publishing</c> or <c>published</c>.</param>
/// <param name="AlreadyCreated">The same design and settings were sent before; that product is returned.</param>
/// <param name="MediaAttached">APCS mock-ups and video added to the Etsy listing.</param>
public sealed record PrintifyProductDto(
    Guid ProductId,
    string PrintifyProductId,
    string Title,
    string State,
    string? EtsyListingId,
    string? EtsyUrl,
    IReadOnlyList<PrintifyProductVariantDto> Variants,
    decimal? UsShippingFirstItem,
    bool AlreadyCreated,
    int MediaAttached,
    IReadOnlyList<string> Warnings);

/// <param name="Cost">What Printify charges to make the item, without shipping.</param>
/// <param name="EstimatedFees">Etsy's listing, transaction and payment fees at this price plus US shipping (an estimate).</param>
/// <param name="EstimatedProfit">Price minus cost minus fees.</param>
public sealed record PrintifyProductVariantDto(
    long Id,
    string Title,
    decimal Cost,
    decimal Price,
    bool IsEnabled,
    decimal EstimatedFees,
    decimal EstimatedProfit);
