using APCS.Application.Abstractions.Storage;
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

    // Local "flat fabric" brightness: wide enough to keep soft, broad folds, narrow enough to follow lighting.
    internal const double FabricSigmaRatio = 0.10;
    // Relief is smoothed so the design bends with folds rather than kinking at seams and stitching.
    internal const double ReliefSigmaRatio = 0.012;
    internal const double DisplacementGain = 6.0;

    // Fold shadows on a lit white garment are faint, so they are deepened to show once recolored.
    internal const double ShadingGain = 1.5;

    public bool IsAvailable => segmenter.IsAvailable;

    public byte[] GenerateDisplacementMap(byte[] baseImage)
    {
        using var source = Image.Load<Rgba32>(baseImage);
        using var working = ScaledCopy(source, WorkingMaxSide);
        var (w, h) = (working.Width, working.Height);

        var luminance = Pixels(working).Select(p => (float)Luma(p)).ToArray();
        var shortSide = Math.Min(w, h);
        var fabric = GaussianBlur(luminance, w, h, shortSide * FabricSigmaRatio);
        var relief = GaussianBlur(luminance, w, h, shortSide * ReliefSigmaRatio);

        using var displacement = new Image<Rgb24>(w, h);
        for (var i = 0; i < luminance.Length; i++)
        {
            var offset = ToByte(128 + DisplacementGain * (relief[i] - fabric[i]) * 255);
            displacement[i % w, i / w] = new Rgb24(offset, offset, 128);
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

        // RGB carries the fabric's shading relative to its brightest parts, so one multiply of this
        // image re-applies folds and texture on top of a flat garment color and the design.
        var white = BrightLevel(full, alpha);
        using var mask = new Image<Rgba32>(width, height);
        for (var i = 0; i < full.Length; i++)
        {
            var shade = ToByte(Math.Clamp(1 - ShadingGain * (1 - Luma(full[i]) / white), 0, 1) * 255);
            mask[i % width, i / width] = new Rgba32(shade, shade, shade, alpha[i]);
        }

        return new GarmentMaskResult(EncodePng(mask), covered / alpha.Length, covered > 0 ? luminanceSum / covered : 0, width, height);
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
