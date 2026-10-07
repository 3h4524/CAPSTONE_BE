using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static APCS.Infrastructure.Services.ImageMaskOps;

namespace APCS.Infrastructure.Services;

/// <summary>Derives the displacement map and the garment mask from a mock-up base photo.</summary>
/// <remarks>
/// Both are produced at the photo's own size, so they line up pixel for pixel with the print area
/// coordinates. Smooth parts are computed on a scaled-down copy and scaled back up.
/// </remarks>
internal sealed class ImageSharpMockupMapGenerator(IGarmentSegmenter segmenter) : IMockupMapGenerator
{
    internal const int WorkingMaxSide = 1600;

    // Relief is smoothed so the design bends with folds rather than kinking at seams and stitching.
    internal const double ReliefSigmaRatio = 0.012;
    // How hard a slope in the fabric's brightness pushes the print. Only a sharp crease comes near
    // the full shift; a faint fold barely moves it, or a round design visibly loses its shape there.
    internal const double DisplacementGain = 8.0;
    // The shift fades out over this many relief widths toward the garment's edge, where the change
    // in brightness is the garment's outline and not a fold.
    internal const double EdgeFadeWidths = 2.0;

    // Fold shadows on a lit white garment are faint, so they are deepened to show once recolored.
    internal const double ShadingGain = 1.5;

    public bool IsAvailable => segmenter.IsAvailable;

    public byte[] GenerateDisplacementMap(byte[] baseImage)
    {
        using var source = Image.Load<Rgba32>(baseImage);
        using var working = ScaledCopy(source, WorkingMaxSide);
        var (w, h) = (working.Width, working.Height);

        var matte = segmenter.Segment(source, w, h)
            ?? throw new InvalidOperationException("The segmentation model could not process this photo.");
        var garment = matte.Select(a => a / 255f).ToArray();
        var luminance = Pixels(working).Select(p => (float)Luma(p)).ToArray();

        // Averaged over the garment only, so the background's brightness never reads as relief.
        var sigma = Math.Min(w, h) * ReliefSigmaRatio;
        var lit = GaussianBlur(luminance.Select((value, i) => value * garment[i]).ToArray(), w, h, sigma);
        var share = GaussianBlur(garment, w, h, sigma);
        var relief = lit.Select((value, i) => share[i] > 0.05f ? value / share[i] : 0f).ToArray();
        var inside = GaussianBlur(garment, w, h, sigma * EdgeFadeWidths);

        using var displacement = new Image<Rgb24>(w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var weight = Math.Clamp((inside[i] - 0.5) * 2, 0, 1) * garment[i];
                // The print is drawn toward the darker side, so into a crease from both of its
                // sides, as fabric dipping into a fold does. Sideways for an upright fold, never
                // along it.
                var slopeX = (relief[y * w + Math.Min(w - 1, x + 1)] - relief[y * w + Math.Max(0, x - 1)]) / 2 * sigma;
                var slopeY = (relief[Math.Min(h - 1, y + 1) * w + x] - relief[Math.Max(0, y - 1) * w + x]) / 2 * sigma;
                displacement[x, y] = new Rgb24(
                    ToByte(128 + 127 * Math.Tanh(DisplacementGain * slopeX) * weight),
                    ToByte(128 + 127 * Math.Tanh(DisplacementGain * slopeY) * weight),
                    128);
            }
        }

        if (w != source.Width || h != source.Height)
            displacement.Mutate(c => c.Resize(source.Width, source.Height, KnownResamplers.Triangle));
        return EncodePng(displacement);
    }

    public GarmentMaskResult GenerateGarmentMask(byte[] baseImage, int? maxOutputSide = null)
    {
        using var loaded = Image.Load<Rgba32>(baseImage);
        using var scaled = maxOutputSide is int limit ? ScaledCopy(loaded, limit) : null;
        var source = scaled ?? loaded;
        var (width, height) = (source.Width, source.Height);
        var full = Pixels(source);

        // The model's matte is used as it comes. A preview asks for the quick one.
        var alpha = segmenter.Segment(loaded, width, height, quick: maxOutputSide is not null)
            ?? throw new InvalidOperationException("The segmentation model could not process this photo.");

        double covered = 0, luminanceSum = 0;
        for (var i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] < 128) continue;
            covered++;
            luminanceSum += Luma(full[i]);
        }

        // A matte covering next to nothing, or nearly everything, told no garment apart. The whole
        // photo then counts as the garment, so a design is never cut to a shape that means nothing.
        var coverage = covered / alpha.Length;
        var separated = MockupRules.IsSeparated(new GarmentMaskStats(coverage, 0));

        // RGB carries the fabric's shading relative to its brightest parts, so one multiply of this
        // image re-applies folds and texture on top of a flat garment color and the design.
        var white = BrightLevel(full, alpha);
        using var mask = new Image<Rgba32>(width, height);
        for (var i = 0; i < full.Length; i++)
        {
            var shade = ToByte(Math.Clamp(1 - ShadingGain * (1 - Luma(full[i]) / white), 0, 1) * 255);
            mask[i % width, i / width] = new Rgba32(shade, shade, shade, separated ? alpha[i] : byte.MaxValue);
        }

        return new GarmentMaskResult(EncodePng(mask), coverage, covered > 0 ? luminanceSum / covered : 0, width, height);
    }

    // The 95th percentile of the garment's brightness, so a few clipped highlights don't set it.
    private static double BrightLevel(Rgba32[] pixels, byte[] alpha)
    {
        var samples = new List<double>();
        for (var i = 0; i < pixels.Length; i += 7)
            if (alpha[i] > 200) samples.Add(Luma(pixels[i]));
        if (samples.Count == 0) return 1;
        samples.Sort();
        return Math.Max(0.05, samples[(int)(samples.Count * 0.95)]);
    }
}
