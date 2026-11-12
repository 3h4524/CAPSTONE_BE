namespace APCS.Application.Features.Shops.Dtos.Request;

/// <summary>
/// Creates the product on Printify (not yet on Etsy): one approved design printed on the chosen variants of a
/// blueprint by one provider. Every enabled variant starts at <c>Price</c>; prices are adjusted before publishing.
/// </summary>
/// <param name="Scale">The design's width relative to the print area; 1 fills the width.</param>
/// <param name="Position">The print area; <c>front</c> when omitted and the product has one.</param>
/// <param name="X">The design's center across the print area, 0 (left edge) to 1 (right edge).</param>
/// <param name="Y">The design's center down the print area, 0 (top edge) to 1 (bottom edge).</param>
/// <param name="PlacementMockupId">
/// An approved mock-up whose placement the print follows (where and how large the design sits on the template),
/// instead of <paramref name="X"/>, <paramref name="Y"/> and <paramref name="Scale"/>. A mock-up of another side is read
/// as its garment's front.
/// </param>
/// <param name="PrintMockupSides">
/// With <paramref name="PlacementMockupId"/>: the design is also printed on each other side (back, sleeves) the approved
/// mock-ups show it on, where they show it, when the provider prints that side.
/// </param>
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
    string? Position = null,
    double X = 0.5,
    double Y = 0.5,
    Guid? PlacementMockupId = null,
    bool PrintMockupSides = false);

/// <summary>The retail price of each enabled variant, confirmed against Printify's costs; then Printify publishes to Etsy.</summary>
public sealed record PublishPrintifyProductRequestDto(IReadOnlyList<PrintifyVariantPriceDto> Prices);

public sealed record PrintifyVariantPriceDto(long VariantId, decimal Price);

/// <summary>Once Printify's Etsy listing exists, these approved APCS media are added to it.</summary>
/// <param name="AfterPrintifyPhoto">
/// Puts the mock-ups after Printify's first photo instead of in front of it, so the listing opens on the photo of the
/// garment Printify ships; Printify's photos of each color stay on the listing for its variations.
/// </param>
public sealed record SyncPrintifyProductRequestDto(IReadOnlyList<Guid> MockupIds, bool IncludeVideo, bool AfterPrintifyPhoto = false);
