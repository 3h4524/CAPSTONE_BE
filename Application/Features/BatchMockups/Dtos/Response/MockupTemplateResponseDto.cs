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
public sealed record MockupTemplateResponseDto(
    Guid Id,
    string Name,
    string ProductType,
    string BaseImageUrl,
    string? PreviewImageUrl,
    string PrintAreaConfig,
    int OutputWidthPx,
    int OutputHeightPx,
    int UsageCount);
