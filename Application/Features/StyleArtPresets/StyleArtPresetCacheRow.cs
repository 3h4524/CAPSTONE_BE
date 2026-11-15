namespace APCS.Application.Features.StyleArtPresets;

public sealed record StyleArtPresetCacheRow(
    Guid Id,
    string Name,
    string Description,
    string StyleModifiers,
    string? PreviewImageUrl,
    string Recommendations,
    int UsageCount,
    string? Translations);
