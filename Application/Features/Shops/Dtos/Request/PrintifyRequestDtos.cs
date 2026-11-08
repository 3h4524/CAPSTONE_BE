namespace APCS.Application.Features.Shops.Dtos.Request;

/// <summary>
/// Creates the product on Printify (not yet on Etsy): one approved design printed on the chosen variants of a
/// blueprint by one provider. Every enabled variant starts at <c>Price</c>; prices are adjusted before publishing.
/// </summary>
/// <param name="Scale">The design's width relative to the print area; 1 fills the width.</param>
/// <param name="Position">The print area; <c>front</c> when omitted and the product has one.</param>
public sealed record CreatePrintifyProductRequestDto(
    Guid DesignId,
    long BlueprintId,
    string? BlueprintTitle,
    long PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<long> VariantIds,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    decimal Price,
    double Scale = 1,
    string? Position = null);

/// <summary>The retail price of each enabled variant, confirmed against Printify's costs; then Printify publishes to Etsy.</summary>
public sealed record PublishPrintifyProductRequestDto(IReadOnlyList<PrintifyVariantPriceDto> Prices);

public sealed record PrintifyVariantPriceDto(long VariantId, decimal Price);

/// <summary>Once Printify's Etsy listing exists, these approved APCS media are added to it.</summary>
public sealed record SyncPrintifyProductRequestDto(IReadOnlyList<Guid> MockupIds, bool IncludeVideo);
