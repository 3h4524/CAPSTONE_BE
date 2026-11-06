namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>What a not-yet-saved base photo will become once saved.</summary>
/// <param name="Recolorable">Whether the photo passes the recolor checks.</param>
/// <param name="Reason">Why it does not, when it does not.</param>
/// <param name="MaskDataUrl">The garment mask as a PNG data URL, when the photo could be analyzed.</param>
/// <param name="GarmentDataUrl">
/// For a model photo: the garment the person wears, found by the clothes model, as a PNG data URL whose alpha is
/// the garment; the selection to start from. Null when the clothes model is not installed or found none.
/// </param>
/// <param name="GarmentCount">
/// How many separate garments were found: one per person standing apart, each of whom will wear the design.
/// </param>
public sealed record GarmentMaskPreviewResponseDto(bool Recolorable, string? Reason, string? MaskDataUrl, string? GarmentDataUrl = null, int? GarmentCount = null);
