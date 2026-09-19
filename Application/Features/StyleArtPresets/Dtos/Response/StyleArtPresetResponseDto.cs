namespace APCS.Application.Features.StyleArtPresets.Dtos.Response;

/// <summary>An art style as shown in the select step and the seller management screen.</summary>
/// <param name="Id">The selected style id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The detailed description.</param>
/// <param name="StyleModifiers">Keywords appended to the image prompt.</param>
/// <param name="PreviewImageUrl">The preview image URL, when one was uploaded.</param>
/// <param name="Recommendations">Suggested use cases.</param>
/// <param name="IsSystemTemplate">Whether this is a system-provided style.</param>
/// <param name="IsMine">Whether the current seller created this style.</param>
public sealed record StyleArtPresetResponseDto(
    Guid Id,
    string Name,
    string Description,
    string StyleModifiers,
    string? PreviewImageUrl,
    IReadOnlyList<string> Recommendations,
    bool IsSystemTemplate,
    bool IsMine);
