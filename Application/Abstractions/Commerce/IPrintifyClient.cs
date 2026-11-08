namespace APCS.Application.Abstractions.Commerce;

/// <summary>A store in the seller's Printify account; <c>SalesChannel</c> is <c>etsy</c> for one linked to Etsy.</summary>
public sealed record PrintifyShop(long Id, string Title, string SalesChannel);

/// <summary>A product type of the Printify catalog, such as a Gildan 64000 T-shirt.</summary>
public sealed record PrintifyBlueprint(long Id, string Title, string Brand, string Model, string? ImageUrl);

/// <summary>A print provider (a print shop) with where it ships from.</summary>
public sealed record PrintifyProvider(long Id, string Title, string? Country, string? Region);

/// <summary>A color and size combination a provider makes for a blueprint.</summary>
public sealed record PrintifyCatalogVariant(long Id, string Title, string? Color, string? Size, IReadOnlyList<PrintifyPlaceholder> Placeholders);

/// <summary>A print area of a variant (<c>front</c>, <c>back</c>…) and its size in pixels.</summary>
public sealed record PrintifyPlaceholder(string Position, int Width, int Height);

/// <summary>A provider's shipping rate for some variants to some countries, in cents.</summary>
public sealed record PrintifyShippingRate(IReadOnlyList<long> VariantIds, IReadOnlyList<string> Countries, long FirstItemCents, long AdditionalItemCents, string Currency);

public sealed record PrintifyUploadedImage(string Id, int Width, int Height);

/// <summary>A variant of a new product: its retail price in cents and whether it is sold.</summary>
public sealed record PrintifyVariantPrice(long Id, long PriceCents, bool IsEnabled);

/// <summary>A product to create: one design centered on one print area of every chosen variant.</summary>
/// <param name="Scale">The design's width relative to the print area's width; 1 fills it.</param>
public sealed record PrintifyNewProduct(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    long BlueprintId,
    long PrintProviderId,
    IReadOnlyList<PrintifyVariantPrice> Variants,
    string ImageId,
    string Position,
    double Scale);

/// <summary>A product variant with what Printify charges (<c>CostCents</c>) and what the buyer pays (<c>PriceCents</c>).</summary>
public sealed record PrintifyProductVariant(long Id, string Title, long CostCents, long PriceCents, bool IsEnabled, bool IsAvailable);

/// <summary>
/// A product in a Printify store. <c>ExternalId</c> is the listing on the sales channel (the Etsy listing id)
/// once publishing finished; <c>IsLocked</c> is true while Printify is publishing it.
/// </summary>
public sealed record PrintifyProduct(
    string Id,
    string Title,
    bool IsLocked,
    string? ExternalId,
    string? ExternalHandle,
    IReadOnlyList<PrintifyProductVariant> Variants,
    IReadOnlyList<string> ImageUrls);

public enum PrintifyFailureKind
{
    /// <summary>The token was revoked or expired: the seller must paste a new one.</summary>
    Unauthorized,
    /// <summary>Printify limited the request rate; retry later.</summary>
    RateLimited,
    /// <summary>Printify refused the request, with a reason worth showing (a low-quality image, a disabled variant…).</summary>
    Rejected,
    /// <summary>Printify could not be reached or failed.</summary>
    Unavailable,
}

public sealed class PrintifyApiException(PrintifyFailureKind kind, string message, string? reason = null) : Exception(message)
{
    public PrintifyFailureKind Kind { get; } = kind;

    /// <summary>Printify's explanation of a refused request, safe to show the seller.</summary>
    public string? Reason { get; } = reason;
}

/// <summary>
/// The Printify API with a seller's personal access token. Printify publishes a product to the Etsy store linked
/// to it; that listing is the one Printify fulfils orders for.
/// </summary>
public interface IPrintifyClient
{
    Task<IReadOnlyList<PrintifyShop>> GetShopsAsync(string token, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintifyBlueprint>> GetBlueprintsAsync(string token, CancellationToken cancellationToken);

    /// <summary>Every print provider of the catalog, with its location.</summary>
    Task<IReadOnlyList<PrintifyProvider>> GetProvidersAsync(string token, CancellationToken cancellationToken);

    /// <summary>The providers that make <paramref name="blueprintId"/>; only id and title are filled.</summary>
    Task<IReadOnlyList<PrintifyProvider>> GetBlueprintProvidersAsync(string token, long blueprintId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintifyCatalogVariant>> GetVariantsAsync(string token, long blueprintId, long providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrintifyShippingRate>> GetShippingAsync(string token, long blueprintId, long providerId, CancellationToken cancellationToken);

    /// <summary>Printify fetches the image from <paramref name="sourceUrl"/>, the recommended way for large files.</summary>
    Task<PrintifyUploadedImage> UploadImageAsync(string token, string fileName, string sourceUrl, CancellationToken cancellationToken);

    Task<PrintifyProduct> CreateProductAsync(string token, long shopId, PrintifyNewProduct product, CancellationToken cancellationToken);

    Task<PrintifyProduct> GetProductAsync(string token, long shopId, string productId, CancellationToken cancellationToken);

    Task<PrintifyProduct> UpdatePricesAsync(string token, long shopId, string productId, IReadOnlyList<PrintifyVariantPrice> variants, CancellationToken cancellationToken);

    /// <summary>Asks Printify to publish the product to the store's sales channel; it finishes in the background.</summary>
    Task PublishAsync(string token, long shopId, string productId, CancellationToken cancellationToken);
}
