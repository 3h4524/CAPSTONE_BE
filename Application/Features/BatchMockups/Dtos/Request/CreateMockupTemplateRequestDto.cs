using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Creates a seller-owned mock-up template.</summary>
/// <param name="Name">The display name.</param>
/// <param name="ProductType">The compatible product type.</param>
/// <param name="PrintAreaConfig">The overlay coordinates as a JSON string.</param>
/// <param name="OutputWidthPx">The output image width in pixels.</param>
/// <param name="OutputHeightPx">The output image height in pixels.</param>
/// <param name="BaseImage">The blank mock-up image upload.</param>
/// <param name="Preview">An optional preview image upload.</param>
public sealed record CreateMockupTemplateRequestDto(
    string Name,
    string ProductType,
    string PrintAreaConfig,
    int OutputWidthPx,
    int OutputHeightPx,
    UploadFileDto? BaseImage,
    UploadFileDto? Preview);
