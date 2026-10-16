namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>A mock-up template as shown in the batch setup picker.</summary>
/// <param name="Id">The template id.</param>
/// <param name="Name">The display name.</param>
/// <param name="ProductType">The compatible product type.</param>
/// <param name="BaseImageUrl">The blank mock-up image URL.</param>
/// <param name="PreviewImageUrl">The preview image URL, when one was uploaded.</param>
/// <param name="PrintAreaConfig">The overlay coordinates as a JSON string.</param>
/// <param name="OutputWidthPx">The output image width in pixels.</param>
/// <param name="OutputHeightPx">The output image height in pixels.</param>
/// <param name="UsageCount">How often this template was used, for ordering.</param>
/// <param name="IsSystemTemplate">Whether this is a system-provided template.</param>
/// <param name="IsMine">Whether the current seller created this template.</param>
/// <param name="RealisticPrintReady">Whether shading and fabric-relief maps exist for the current photo.</param>
/// <param name="AllowRecolor">Whether the garment can be recolored.</param>
/// <param name="GarmentMaskUrl">The garment mask image, for placing the print area and live recolor previews; null until the photo has been analyzed.</param>
/// <param name="GarmentColor">The template's own garment color, used when a batch picks no colors; null keeps the photo's color.</param>
public sealed record MockupTemplateResponseDto(
    Guid Id,
    string Name,
    string ProductType,
    string BaseImageUrl,
    string? PreviewImageUrl,
    string PrintAreaConfig,
    int OutputWidthPx,
    int OutputHeightPx,
    int UsageCount,
    bool IsSystemTemplate,
    bool IsMine,
    bool RealisticPrintReady,
    bool AllowRecolor,
    string? GarmentMaskUrl,
    string? GarmentColor);
