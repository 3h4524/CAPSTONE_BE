namespace APCS.Application.Abstractions.Storage;

/// <summary>
/// Precomputes the helper images a mock-up template needs for realistic compositing. They are
/// derived once from the template's base photo and stored next to it, so each composite stays a
/// plain delivery URL.
/// </summary>
public interface IMockupMapGenerator
{
    /// <summary>Whether the model that finds the garment is installed; without it no photo can be processed.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// How the garment's folds shift a print, at the photo's own size: R holds the sideways shift
    /// and G the vertical one, 128 is none. Neutral off the garment.
    /// </summary>
    byte[] GenerateDisplacementMap(byte[] baseImage);

    /// <summary>
    /// Separates the garment from a plain background. The PNG's alpha is the garment and its gray
    /// RGB the fabric's shading (white where the garment is brightest). When no garment could be
    /// told apart, the whole photo is the garment.
    /// </summary>
    /// <param name="baseImage">The photo.</param>
    /// <param name="maxOutputSide">Scales the result down to this longest side, for a quick preview.</param>
    GarmentMaskResult GenerateGarmentMask(byte[] baseImage, int? maxOutputSide = null);
}

/// <summary>
/// The garment mask plus what is needed to judge whether recoloring will look right.
/// </summary>
/// <param name="MaskPng">PNG bytes of the mask.</param>
/// <param name="GarmentCoverage">Share of the photo covered by the garment, 0..1.</param>
/// <param name="GarmentLuminance">Average brightness of the garment, 0..1.</param>
/// <param name="WidthPx">Width of the photo as the mask describes it.</param>
/// <param name="HeightPx">Height of the photo as the mask describes it.</param>
public sealed record GarmentMaskResult(
    byte[] MaskPng, double GarmentCoverage, double GarmentLuminance, int WidthPx = 0, int HeightPx = 0);

/// <summary>Removes the plain backdrop around generated artwork.</summary>
public interface IDesignBackgroundRemover
{
    /// <summary>
    /// A PNG with the backdrop transparent, or <see langword="null"/> when no clear backdrop was
    /// found (the image should then be used as is).
    /// </summary>
    byte[]? RemoveBackground(byte[] image);
}
