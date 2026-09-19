namespace APCS.Application.Features.StyleArtPresets.Dtos.Response;

public sealed record StyleArtPresetResponseDto(
    Guid Id,
    string Name,
    string Description,
    string StyleModifiers,
    string? PreviewImageUrl,
    IReadOnlyList<string> Recommendations,
    bool IsSystemTemplate,
    bool IsMine);
