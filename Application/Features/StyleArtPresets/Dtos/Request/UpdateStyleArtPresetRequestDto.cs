using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.StyleArtPresets.Dtos.Request;

public sealed record UpdateStyleArtPresetRequestDto(
    string Name,
    string Description,
    string StyleModifiers,
    string? RecommendationsJson,
    UploadFileDto? Preview,
    bool DeletePreview);
