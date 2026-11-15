namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>
/// The Printify product a template's photo shows. Its print area is Printify's print area for this blueprint and
/// provider, so a design placed in it prints where the mock-up shows it.
/// </summary>
/// <param name="Colors">The blueprint's garment colors: Printify's name and the hex that recolors this photo to look like Printify's own mock-up.</param>
public sealed record MockupTemplatePrintifyDto(
    long BlueprintId,
    string BlueprintTitle,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string> DefaultSizes,
    IReadOnlyList<PrintifyGarmentColorDto> Colors,
    string? BlueprintImageUrl = null);

/// <summary>A Printify garment color: its name on Printify and the upper-case <c>#RRGGBB</c> a mock-up is recolored with.</summary>
public sealed record PrintifyGarmentColorDto(string Name, string Hex);
