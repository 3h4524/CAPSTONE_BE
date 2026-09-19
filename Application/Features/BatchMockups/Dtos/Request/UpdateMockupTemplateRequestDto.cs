using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Updates a seller-owned mock-up template.</summary>
/// <param name="Name">The display name.</param>
/// <param name="ProductType">The compatible product type.</param>
/// <param name="PrintAreaConfig">The overlay coordinates as a JSON string.</param>
/// <param name="OutputWidthPx">The output image width in pixels.</param>
/// <param name="OutputHeightPx">The output image height in pixels.</param>
/// <param name="BaseImage">A replacement blank image, when provided.</param>
/// <param name="Preview">A replacement preview image, when provided.</param>
/// <param name="DeletePreview">Removes the current preview image when no replacement is provided.</param>
public sealed record UpdateMockupTemplateRequestDto(
    string Name,
    string ProductType,
    string PrintAreaConfig,
    int OutputWidthPx,
    int OutputHeightPx,
    UploadFileDto? BaseImage,
    UploadFileDto? Preview,
    bool DeletePreview);
