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
internal sealed class ImageSharpMockupMapGenerator(IGarmentSegmenter segmenter, IClothesSegmenter? clothes = null) : IMockupMapGenerator
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

    // How far the cut mask's edge is pulled in. One pixel removes the line of old background; more only shrinks the garment.
    internal const int CutMaskInset = 1;

    public bool IsAvailable => segmenter.IsAvailable;

    public byte[] GenerateDisplacementMap(byte[] baseImage, byte[]? garmentSelection = null)
    {
        using var source = LoadPhoto(baseImage);
        using var working = ScaledCopy(source, WorkingMaxSide);
        var (w, h) = (working.Width, working.Height);

        var matte = GarmentMatte(source, Pixels(working), w, h, garmentSelection);
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

    public GarmentMaskResult GenerateGarmentMask(byte[] baseImage, int? maxOutputSide = null, byte[]? garmentSelection = null)
    {
        using var loaded = LoadPhoto(baseImage);
        using var scaled = maxOutputSide is int limit ? ScaledCopy(loaded, limit) : null;
        var source = scaled ?? loaded;
        var (width, height) = (source.Width, source.Height);
        var full = Pixels(source);

        // The model's matte is used as it comes. A preview asks for the quick one.
        var alpha = GarmentMatte(loaded, full, width, height, garmentSelection, quick: maxOutputSide is not null);

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
        // A model photo's garment is what the seller selected, so it is kept even when small; the
        // service refuses a selection too small to use.
        var separated = garmentSelection is not null || MockupRules.IsSeparated(new GarmentMaskStats(coverage, 0));

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

    public byte[]? GenerateCutMask(byte[] garmentMaskPng)
    {
        using var mask = LoadPhoto(garmentMaskPng);
        var (width, height) = (mask.Width, mask.Height);
        var pixels = Pixels(mask);

        var covered = pixels.Count(p => p.A >= 128);
        if (!MockupRules.IsSeparated(new GarmentMaskStats((double)covered / pixels.Length, 0)))
            return null;

        // Each pixel takes the lowest alpha among itself and its neighbours, which moves the edge in by one pixel.
        using var cut = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = byte.MaxValue;
                for (var ny = Math.Max(0, y - CutMaskInset); ny <= Math.Min(height - 1, y + CutMaskInset); ny++)
                    for (var nx = Math.Max(0, x - CutMaskInset); nx <= Math.Min(width - 1, x + CutMaskInset); nx++)
                        alpha = Math.Min(alpha, pixels[ny * width + nx].A);

                var p = pixels[y * width + x];
                cut[x, y] = new Rgba32(p.R, p.G, p.B, alpha);
            }
        }

        return EncodePng(cut);
    }

    /// <summary>
    /// The model's matte, limited to <paramref name="garmentSelection"/> when one is given: on a photo
    /// of a person the model finds the whole person, and the selection says which part is the garment.
    /// The model's soft edge is kept where the garment meets the background.
    /// </summary>
    /// <param name="pixels">The photo's pixels at <paramref name="width"/> x <paramref name="height"/>.</param>
    private byte[] GarmentMatte(Image<Rgba32> photo, Rgba32[] pixels, int width, int height, byte[]? garmentSelection, bool quick = false)
    {
        var matte = segmenter.Segment(photo, width, height, quick)
            ?? throw new InvalidOperationException("The segmentation model could not process this photo.");
        if (garmentSelection is null)
            return matte;

        // Where the matte cut into the garment (white on a white wall), the clothes model puts it back.
        if (clothes is { IsAvailable: true } && clothes.UpperClothes(photo, width, height) is { } upper)
            matte = MendPersonMatte(pixels, matte, upper);

        // A selection brushed by hand or found by the clothes model is a few pixels off where it meets an arm,
        // the neck or other clothes; its edge is moved onto the photo's.
        var selected = SelectionAlpha(garmentSelection, width, height).Select(value => value / 255f).ToArray();
        var garment = SnapGarmentEdge(pixels, selected, matte, width, height);
        for (var i = 0; i < matte.Length; i++)
            matte[i] = ToByte(matte[i] * Math.Clamp(garment[i], 0f, 1f));
        return matte;
    }

    public byte[]? GenerateWornGarmentMask(byte[] photo, int? maxOutputSide = null)
    {
        if (clothes is not { IsAvailable: true })
            return null;

        using var loaded = LoadPhoto(photo);
        using var scaled = maxOutputSide is int limit ? ScaledCopy(loaded, limit) : null;
        var (width, height) = ((scaled ?? loaded).Width, (scaled ?? loaded).Height);

        // The person's outline from the matte, the garment from the clothes model: each is good at what the other
        // is not (a smooth edge against the background; telling a shirt from the arm in front of it).
        var person = segmenter.Segment(loaded, width, height, quick: maxOutputSide is not null);
        var upper = clothes.UpperClothes(loaded, width, height);
        if (upper is null || person is null)
            return null;
        var pixels = Pixels(scaled ?? loaded);
        // Where the matte cut into the garment (white on a white wall), the clothes model puts it back.
        person = MendPersonMatte(pixels, person, upper);

        // The clothes model's answer is a soft guess, coarse at the garment's edge and unsure in a patch of glare
        // or shadow. It is made decisive, and a patch it was unsure of inside the garment is filled.
        var decisive = upper.Select(value => (float)Math.Clamp((value / 255.0 - 0.4) / 0.2, 0, 1)).ToArray();
        // Closing first shuts the narrow channels that would let such a patch reach an arm and count as open.
        var radius = Math.Max(1, (int)Math.Round(Math.Min(width, height) * ClosingRatio));
        var closed = Dilate(Dilate(decisive, width, height, radius).Select(value => 1 - value).ToArray(), width, height, radius)
            .Select(value => (double)(1 - value)).ToArray();
        var filled = FillEnclosedGaps(closed, width, height).Select(value => (float)value).ToArray();
        // The clothes model sees the photo coarsely; the outline is moved onto the photo's own edges.
        var garment = SnapGarmentEdge(pixels, filled, person, width, height);

        using var mask = new Image<Rgba32>(width, height);
        var covered = 0;
        for (var i = 0; i < person.Length; i++)
        {
            var alpha = ToByte(person[i] * garment[i]);
            if (alpha >= 128) covered++;
            mask[i % width, i / width] = new Rgba32(255, 255, 255, alpha);
        }

        // Less than this share of the photo is not a garment worn on the upper body: there is none to suggest.
        return covered < MockupRules.MinimumModelGarmentCoverage * person.Length ? null : EncodePng(mask);
    }

    // Gaps enclosed by the garment up to this share of it are filled; a larger one is something in front of it.
    internal const double EnclosedGapShare = 0.03;

    // The clothes mask is closed (grown, then shrunk back) by this share of the photo's shorter side.
    internal const double ClosingRatio = 0.012;

    /// <summary>
    /// The garment with the small gaps it encloses filled: patches the clothes model was unsure of (glare, a deep
    /// fold). A gap that reaches the photo's edge, or is large, is left: it is an arm or a hand.
    /// </summary>
    internal static double[] FillEnclosedGaps(double[] garment, int width, int height)
    {
        var outside = FloodFillFromBorder(garment.Select(value => value < 0.5).ToArray(), width, height);
        var garmentArea = garment.Count(value => value >= 0.5);
        var filled = (double[])garment.Clone();
        var seen = new bool[garment.Length];
        var queue = new Queue<int>();
        for (var start = 0; start < garment.Length; start++)
        {
            if (seen[start] || outside[start] || garment[start] >= 0.5) continue;
            // One enclosed gap, walked whole to measure it.
            var gap = new List<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                gap.Add(i);
                int x = i % width, y = i / width;
                foreach (var next in new[] { x > 0 ? i - 1 : -1, x < width - 1 ? i + 1 : -1, y > 0 ? i - width : -1, y < height - 1 ? i + width : -1 })
                {
                    if (next < 0 || seen[next] || garment[next] >= 0.5) continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (gap.Count <= garmentArea * EnclosedGapShare)
                foreach (var i in gap) filled[i] = 1;
        }
        return filled;
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
