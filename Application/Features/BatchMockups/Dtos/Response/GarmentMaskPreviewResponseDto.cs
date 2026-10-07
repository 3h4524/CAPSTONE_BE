namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>What a not-yet-saved base photo will become once saved.</summary>
/// <param name="Recolorable">Whether the photo passes the recolor checks.</param>
/// <param name="Reason">Why it does not, when it does not.</param>
/// <param name="MaskDataUrl">The garment mask as a PNG data URL, when the photo could be analyzed.</param>
public sealed record GarmentMaskPreviewResponseDto(bool Recolorable, string? Reason, string? MaskDataUrl);
