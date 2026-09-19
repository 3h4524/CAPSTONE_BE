using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.StyleArtPresets.Dtos.Request;

/// <summary>Creates a seller-owned art style.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">The detailed description.</param>
/// <param name="StyleModifiers">Keywords appended to the image prompt.</param>
/// <param name="RecommendationsJson">Suggested use cases as a JSON string array.</param>
/// <param name="Preview">The preview image upload.</param>
public sealed record CreateStyleArtPresetRequestDto(
    string Name,
    string Description,
    string StyleModifiers,
    string? RecommendationsJson,
    UploadFileDto? Preview);
