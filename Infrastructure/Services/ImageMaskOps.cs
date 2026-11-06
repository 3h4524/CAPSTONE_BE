using APCS.Application.Abstractions.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace APCS.Infrastructure.Services;

/// <summary>Pixel operations shared by the mock-up map generator and the design background remover.</summary>
internal static class ImageMaskOps
{
    // Background pixels are close to the image's border color (max RGB distance, 0..441) and flat
    // (luminance change to their neighbours after light denoising, 0..1). An outline, however faint, is not flat.
    internal const double BackgroundTolerance = 12;
    internal const float BackgroundMaxGradient = 0.006f;

    // Gaps narrower than twice this in an outline are sealed, so the background fill cannot leak
    // into a subject whose lit side is as bright as the background.
    internal const int OutlineGapRadius = 3;

    // How far denoising can spread a high-contrast edge's gradient, in working pixels.
    private const int MaximumEdgeHalo = 6;

    // Width, in working pixels, of the ring of compression noise around a high-contrast edge (a JPEG block).
    private const int CompressionRing = 8;
    private const double RingToleranceShare = 0.3;
    private const double MaximumRingTolerance = 40;

    // Luminance change, in the photo as shot, below which a background-colored pixel is not on an edge.
    internal const float PlainMaxGradient = 0.008f;

    // Masks are found at this size so the pixel thresholds above hold for any image.
    internal const int MaskWorkingMaxSide = 800;

    // A photo is held uncompressed while it is analyzed (4 bytes per pixel, more than once), so a
    // small file that declares a huge picture is refused instead of filling the memory.
    internal const long MaximumPhotoPixels = 40_000_000;

    // The only formats read here, and only their first frame. A photo comes from an upload, so the
    // library's rarely used decoders (TIFF and the like) are kept away from it; a photo in another
    // format is read from the JPEG the CDN makes of it instead.
    private static readonly DecoderOptions PhotoDecoding = new()
    {
        Configuration = new Configuration(new PngConfigurationModule(), new JpegConfigurationModule(), new WebpConfigurationModule()),
        MaxFrames = 1,
    };

    /// <summary>Opens a PNG, JPEG or WebP photo of at most <see cref="MaximumPhotoPixels"/> pixels.</summary>
    /// <exception cref="UnknownImageFormatException">The bytes are in none of those formats.</exception>
    /// <exception cref="InvalidOperationException">The photo has too many pixels.</exception>
    internal static Image<Rgba32> LoadPhoto(byte[] bytes)
    {
        var info = Image.Identify(PhotoDecoding, bytes);
        if ((long)info.Width * info.Height > MaximumPhotoPixels)
            throw new InvalidOperationException($"The photo is {info.Width} x {info.Height} pixels, more than can be analyzed.");

        return Image.Load<Rgba32>(PhotoDecoding, bytes);
    }

    internal static Image<Rgba32> ScaledCopy(Image<Rgba32> source, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        var copy = source.Clone();
        if (scale < 1.0)
        {
            copy.Mutate(c => c.Resize(
                Math.Max(1, (int)Math.Round(source.Width * scale)),
                Math.Max(1, (int)Math.Round(source.Height * scale))));
        }

        return copy;
    }

    internal static byte[] EncodePng<TPixel>(Image<TPixel> image) where TPixel : unmanaged, IPixel<TPixel>
    {
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }

    internal static Rgba32[] Pixels(Image<Rgba32> image)
    {
        var pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    internal static double Luma(Rgba32 p) => (0.299 * p.R + 0.587 * p.G + 0.114 * p.B) / 255.0;

    internal static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>
    /// Subject = everything not reachable from the image border through background-like pixels.
    /// The outline is thickened by <see cref="OutlineGapRadius"/> while filling and the fill grown
    /// back afterwards, so a gap in a faint outline does not let it leak inside.
    /// </summary>
    internal static float[] FindSubject(Rgba32[] pixels, int w, int h)
    {
        var color = MedianBorderColor(pixels, w, h);
        var luminance = GaussianBlur(pixels.Select(p => (float)Luma(p)).ToArray(), w, h, 1.0);

        var sharp = pixels.Select(p => (float)Luma(p)).ToArray();
        var candidate = new float[w * h];
        var distances = new double[w * h];
        // Background-colored and flat in the photo as shot (not denoised), so an edge counts only on
        // the pixels that actually touch it.
        var plain = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var p = pixels[i];
                double dr = p.R - color.R, dg = p.G - color.G, db = p.B - color.B;
                distances[i] = Math.Sqrt(dr * dr + dg * dg + db * db);
                candidate[i] = Gradient(luminance, x, y, w, h) <= BackgroundMaxGradient && distances[i] <= BackgroundTolerance ? 1f : 0f;
                plain[i] = distances[i] <= BackgroundTolerance && Gradient(sharp, x, y, w, h) <= PlainMaxGradient;
            }
        }

        var walls = Dilate(candidate.Select(v => 1f - v).ToArray(), w, h, OutlineGapRadius);
        var reached = FloodFillFromBorder(walls.Select(v => v < 0.5f).ToArray(), w, h);
        // A strip of background squeezed between the subject and the image border is all "wall", so
        // the fill never starts there; the border's own plain pixels are background all the same.
        MarkPlainBorder(reached, plain, w, h);
        // The fill stops short of every edge (by the gap seal plus the denoising halo, which widens
        // with contrast). It is grown back through plain pixels, so it stops where the edge really is.
        GrowThrough(reached, plain, OutlineGapRadius + MaximumEdgeHalo, w, h);
        // Compression noise next to a high-contrast edge is not "flat" yet is still the background's
        // color; a few more steps through such pixels close the remaining ring around the subject.
        // How far off the background's color that noise may be scales with how far the subject is:
        // tight for white on white, generous for gray on white.
        var ringTolerance = Math.Clamp(SubjectDistance(reached, distances) * RingToleranceShare, BackgroundTolerance / 2, MaximumRingTolerance);
        GrowThrough(reached, distances.Select(d => d <= ringTolerance).ToArray(), CompressionRing, w, h);

        var subject = new float[w * h];
        for (var i = 0; i < subject.Length; i++) subject[i] = reached[i] ? 0f : 1f;
        return subject;
    }

    // The typical color distance of what is not background so far (its median, sampled).
    private static double SubjectDistance(bool[] reached, double[] distances)
    {
        var samples = new List<double>();
        for (var i = 0; i < reached.Length; i += 5)
            if (!reached[i]) samples.Add(distances[i]);
        if (samples.Count == 0) return 0;
        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static float Gradient(float[] luminance, int x, int y, int w, int h)
    {
        var i = y * w + x;
        var gx = x > 0 && x < w - 1 ? luminance[i + 1] - luminance[i - 1] : 0f;
        var gy = y > 0 && y < h - 1 ? luminance[i + w] - luminance[i - w] : 0f;
        return MathF.Sqrt(gx * gx + gy * gy);
    }

    private static void MarkPlainBorder(bool[] reached, bool[] plain, int w, int h)
    {
        for (var x = 0; x < w; x++)
        {
            reached[x] |= plain[x];
            reached[(h - 1) * w + x] |= plain[(h - 1) * w + x];
        }

        for (var y = 0; y < h; y++)
        {
            reached[y * w] |= plain[y * w];
            reached[y * w + w - 1] |= plain[y * w + w - 1];
        }
    }

    // Extends <paramref name="reached"/> in place by up to <paramref name="steps"/> 4-neighbour steps through allowed pixels.
    private static void GrowThrough(bool[] reached, bool[] allowed, int steps, int w, int h)
    {
        var frontier = new List<int>();
        for (var i = 0; i < reached.Length; i++)
            if (reached[i]) frontier.Add(i);

        for (var step = 0; step < steps && frontier.Count > 0; step++)
        {
            var next = new List<int>();
            void Visit(int i)
            {
                if (reached[i] || !allowed[i]) return;
                reached[i] = true;
                next.Add(i);
            }

            foreach (var i in frontier)
            {
                int x = i % w, y = i / w;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }

            frontier = next;
        }
    }

    /// <summary>
    /// Scales a 0..1 working-size mask to the full image size and narrows the edge ramp the
    /// upscale smears, so the edge stays crisp on large photos.
    /// </summary>
    internal static byte[] UpscaleAlpha(float[] mask, int w, int h, int width, int height)
    {
        using var small = new Image<L8>(w, h);
        for (var i = 0; i < mask.Length; i++) small[i % w, i / w] = new L8(ToByte(mask[i] * 255));
        if (w != width || h != height) small.Mutate(c => c.Resize(width, height, KnownResamplers.Triangle));

        var values = new L8[width * height];
        small.CopyPixelDataTo(values);
        var alpha = new byte[values.Length];
        for (var i = 0; i < alpha.Length; i++) alpha[i] = ToByte(Math.Clamp((values[i].PackedValue / 255.0 - 0.35) / 0.3, 0, 1) * 255);
        return alpha;
    }

    /// <summary>
    /// The alpha of a selection image drawn over the photo (alpha = selected), at
    /// <paramref name="width"/> x <paramref name="height"/>. A selection drawn on a smaller copy of
    /// the photo is scaled up with its edge kept crisp, as <see cref="UpscaleAlpha"/> does.
    /// </summary>
    /// <exception cref="UnknownImageFormatException">The bytes are not a PNG, JPEG or WebP image.</exception>
    internal static byte[] SelectionAlpha(byte[] selectionPng, int width, int height)
    {
        using var selection = LoadPhoto(selectionPng);
        var scaled = selection.Width != width || selection.Height != height;
        if (scaled)
            selection.Mutate(c => c.Resize(width, height, KnownResamplers.Triangle));

        var pixels = Pixels(selection);
        var alpha = new byte[pixels.Length];
        for (var i = 0; i < alpha.Length; i++)
        {
            var value = pixels[i].A / 255.0;
            alpha[i] = scaled ? ToByte(Math.Clamp((value - 0.35) / 0.3, 0, 1) * 255) : pixels[i].A;
        }

        return alpha;
    }

    // The body is measured on these rows of the garment's height: below the sleeves, above the hem.
    private const double BodyRowsFrom = 0.55;
    private const double BodyRowsTo = 0.65;

    // On a photo of several people, a separate part of the mask this much smaller than the largest is not a garment
    // of its own (a sleeve cut off by an arm, a speck).
    internal const double SeparateGarmentShare = 0.15;
    // At most this many garments are measured on one photo, so its composite stays a sensible length.
    internal const int MaximumGarments = 6;

    /// <summary>
    /// Each garment on a photo of several people, measured as <see cref="MeasureGarment"/> measures one, from left to
    /// right. The garments are the separate parts of the mask, so people standing apart; two whose garments touch
    /// count as one.
    /// </summary>
    internal static IReadOnlyList<GarmentMeasurement> MeasureGarments(byte[] alpha, int width, int height)
    {
        var label = new int[alpha.Length];
        var parts = new List<List<int>>();
        var queue = new Queue<int>();
        for (var start = 0; start < alpha.Length; start++)
        {
            if (alpha[start] < 128 || label[start] != 0) continue;
            var part = new List<int>();
            label[start] = parts.Count + 1;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                part.Add(i);
                int x = i % width, y = i / width;
                foreach (var next in new[] { x > 0 ? i - 1 : -1, x < width - 1 ? i + 1 : -1, y > 0 ? i - width : -1, y < height - 1 ? i + width : -1 })
                {
                    if (next < 0 || alpha[next] < 128 || label[next] != 0) continue;
                    label[next] = parts.Count + 1;
                    queue.Enqueue(next);
                }
            }
            parts.Add(part);
        }
        if (parts.Count == 0)
            return [];

        var largest = parts.Max(part => part.Count);
        var measured = new List<GarmentMeasurement>();
        foreach (var part in parts.Where(part => part.Count >= largest * SeparateGarmentShare).OrderByDescending(part => part.Count).Take(MaximumGarments))
        {
            var only = new byte[alpha.Length];
            foreach (var i in part) only[i] = alpha[i];
            if (MeasureGarment(only, width, height) is { } garment)
                measured.Add(garment);
        }
        return measured.OrderBy(garment => garment.CenterX).ToList();
    }

    /// <summary>
    /// Where the garment in a mask's opacity sits: its top and bottom rows, and the center and width of its body
    /// below the sleeves, as shares of the mask's size. <see langword="null"/> when the mask holds no garment, or
    /// covers nearly the whole photo (the garment was not told apart from the background).
    /// </summary>
    internal static GarmentMeasurement? MeasureGarment(byte[] alpha, int width, int height)
    {
        // A row needs this much garment to count, so a stray speck does not move the top.
        var minimumRow = Math.Max(1, width / 100);
        int top = -1, bottom = -1;
        long covered = 0;
        for (var y = 0; y < height; y++)
        {
            var count = 0;
            for (var x = 0; x < width; x++)
                if (alpha[y * width + x] >= 128) count++;
            covered += count;
            if (count < minimumRow) continue;
            if (top < 0) top = y;
            bottom = y;
        }

        if (top < 0 || bottom - top < 4 || covered > 0.9 * width * height)
            return null;

        // The body's edges on each measured row: an arm across it leaves a gap, but not its outer edges.
        var widths = new List<int>();
        var centers = new List<double>();
        var garmentHeight = bottom - top + 1;
        for (var y = top + (int)(garmentHeight * BodyRowsFrom); y <= top + (int)(garmentHeight * BodyRowsTo); y++)
        {
            int left = -1, right = -1;
            for (var x = 0; x < width; x++)
            {
                if (alpha[y * width + x] < 128) continue;
                if (left < 0) left = x;
                right = x;
            }
            if (left < 0) continue;
            widths.Add(right - left + 1);
            centers.Add((left + right + 1) / 2.0);
        }

        if (widths.Count == 0)
            return null;

        widths.Sort();
        centers.Sort();
        return new GarmentMeasurement(
            (double)top / height,
            (double)(bottom + 1) / height,
            centers[centers.Count / 2] / width,
            (double)widths[widths.Count / 2] / width);
    }

    /// <summary>
    /// The color of a garment in a photo, as '#RRGGBB': the average of its well-lit pixels (between the 60th and
    /// 95th percentile of brightness), so folds and shadows don't darken it and a few highlights don't lighten
    /// it. <see langword="null"/> when the mask holds no garment, or covers nearly the whole photo.
    /// </summary>
    /// <param name="pixels">The photo's pixels.</param>
    /// <param name="alpha">The garment mask's opacity, one value per pixel of the photo.</param>
    internal static string? GarmentColor(Rgba32[] pixels, byte[] alpha)
    {
        var garment = new List<Rgba32>();
        for (var i = 0; i < pixels.Length; i++)
            if (alpha[i] >= 200) garment.Add(pixels[i]);
        if (garment.Count == 0 || garment.Count > 0.9 * pixels.Length)
            return null;

        garment.Sort((left, right) => Luma(left).CompareTo(Luma(right)));
        var from = (int)(garment.Count * 0.6);
        var to = Math.Max(from + 1, (int)(garment.Count * 0.95));
        double r = 0, g = 0, b = 0;
        for (var i = from; i < to; i++)
        {
            r += garment[i].R;
            g += garment[i].G;
            b += garment[i].B;
        }
        var count = to - from;
        return $"#{ToByte(r / count):X2}{ToByte(g / count):X2}{ToByte(b / count):X2}";
    }

    // ---- Moving a rough garment outline onto the photo's own edge ----

    // How far a rough outline may be off, as a share of the photo's longer side. The clothes model sees the photo on
    // a 128-cell grid and is off by up to about a cell and a half; a brushed selection is no closer.
    internal const double GarmentEdgeReachRatio = 1.5 / 128;
    // Pixels of the garment and of the rest of the person nearer than this (in Lab units, lightness counted at half)
    // can't be told apart by color, so there the rough outline stands.
    internal const double DistinctColors = 15;
    // How sharply a pixel goes over to the side it looks more like, where only lightness tells the sides apart.
    internal const double EdgeColorSoftness = 8;
    // The garment and what is next to it differ in hue by at least this (Lab a/b units) where hue tells them apart,
    // as a white shirt and skin do.
    internal const double DistinctHues = 10;
    // How far from the garment's edge a pixel can still hold part of it: the photo's own edge is about this soft.
    private const int EdgeSoftWidth = 2;
    // How steeply the smoothed edge goes from outside the garment to inside it: a straight edge stays a pixel sharp,
    // a stepped one comes out as a smooth line.
    private const double EdgeSharpness = 4;

    /// <summary>
    /// <paramref name="garment"/> (0..1, a rough outline) with its edge moved onto the photo's. Near the outline
    /// each pixel goes to the garment or to the rest of the person by which it looks more like close by: the colors
    /// well inside the outline (surely the garment) or well outside it on the person (skin, hair, other clothes).
    /// Where the garment meets the background it is left whole, since <paramref name="person"/>, the person's
    /// matte, gives that edge once multiplied in.
    /// </summary>
    internal static float[] SnapGarmentEdge(Rgba32[] pixels, float[] garment, byte[] person, int w, int h)
    {
        var reach = Math.Max(2, (int)Math.Ceiling(Math.Max(w, h) * GarmentEdgeReachRatio));
        var inside = garment.Select(value => value >= 0.5f ? 1f : 0f).ToArray();
        // 1 within reach of the garment, and 1 within reach of what is not the garment.
        var nearGarment = Dilate(inside, w, h, reach);
        var nearOutside = Dilate(inside.Select(value => 1 - value).ToArray(), w, h, reach);

        // Sure colors are sampled on a grid, so a pixel compares itself with a fixed number of them at any size.
        var window = 3 * reach;
        var step = Math.Max(1, window / 8);
        var (columns, rows) = ((w + step - 1) / step, (h + step - 1) / step);
        var kind = new sbyte[columns * rows];
        var samples = new (float L, float A, float B)[columns * rows];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var i = row * step * w + column * step;
                var sure = nearOutside[i] == 0 && person[i] >= 128 ? 1
                    : nearGarment[i] == 0 && person[i] >= 230 ? -1
                    : 0;
                kind[row * columns + column] = (sbyte)sure;
                if (sure != 0) samples[row * columns + column] = Lab(pixels[i]);
            }

        var result = new float[w * h];
        Parallel.For(0, h, y =>
        {
            Span<double> garmentNearest = stackalloc double[3];
            Span<double> otherNearest = stackalloc double[3];
            Span<int> garmentAt = stackalloc int[3];
            Span<int> otherAt = stackalloc int[3];
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                if (nearGarment[i] == 0) continue;
                // Well inside the outline, or the background beside it: the person's matte gives the edge there.
                if (nearOutside[i] == 0 || person[i] < 5)
                {
                    result[i] = 1;
                    continue;
                }

                var color = Lab(pixels[i]);
                garmentNearest.Fill(double.MaxValue);
                otherNearest.Fill(double.MaxValue);
                garmentAt.Fill(-1);
                otherAt.Fill(-1);
                var (fromRow, toRow) = (Math.Max(0, (y - window) / step), Math.Min(rows - 1, (y + window) / step));
                var (fromColumn, toColumn) = (Math.Max(0, (x - window) / step), Math.Min(columns - 1, (x + window) / step));
                for (var row = fromRow; row <= toRow; row++)
                    for (var column = fromColumn; column <= toColumn; column++)
                    {
                        var k = row * columns + column;
                        if (kind[k] == 0) continue;
                        var s = samples[k];
                        // Being darker than the garment counts for less: the fabric's shading, multiplied over the
                        // new color, keeps a shadow or a dark edge dark. Skin and other clothes differ in color too.
                        var dl = (color.L - s.L) * (kind[k] > 0 && color.L < s.L ? 0.25 : 0.5);
                        var distance = Math.Sqrt(dl * dl + (color.A - s.A) * (color.A - s.A) + (color.B - s.B) * (color.B - s.B));
                        if (kind[k] > 0) KeepNearest(garmentNearest, garmentAt, distance, k);
                        else KeepNearest(otherNearest, otherAt, distance, k);
                    }

                var toGarment = MeanOfKnown(garmentNearest);
                var toOther = MeanOfKnown(otherNearest);
                if (toGarment is null)
                    result[i] = garment[i];
                else if (toOther is null)
                    result[i] = 1;
                else
                {
                    var (garmentA, garmentB) = MeanHue(samples, garmentAt);
                    var (otherA, otherB) = MeanHue(samples, otherAt);
                    var hueApart = Math.Sqrt((garmentA - otherA) * (garmentA - otherA) + (garmentB - otherB) * (garmentB - otherB));
                    double byColor;
                    if (hueApart >= DistinctHues)
                    {
                        // Told apart by hue, as a white shirt and skin are: a pixel on the edge is part one and part
                        // the other, and takes the garment's part of it, so the edge stays as smooth as in the photo.
                        // Only hue is weighed: a fold's shadow on the garment is darker, not another hue.
                        var share = ((color.A - otherA) * (garmentA - otherA) + (color.B - otherB) * (garmentB - otherB)) / (hueApart * hueApart);
                        byColor = Math.Clamp((share - 0.1) / 0.8, 0, 1);
                    }
                    else
                    {
                        // Told apart by lightness only, as dark trousers or hair are: a pixel on the edge itself is
                        // recolored whole. Recolored by half it keeps half the light garment and shows as a pale line,
                        // while recolored whole it stays dark under the fabric's shading.
                        byColor = Math.Clamp(1 + 2 * (toOther.Value - toGarment.Value) / EdgeColorSoftness, 0, 1);
                    }
                    var trust = Math.Clamp((toOther.Value + toGarment.Value) / DistinctColors, 0, 1);
                    result[i] = (float)(trust * byColor + (1 - trust) * garment[i]);
                }
            }
        });

        // A pixel that only looks like the garment (a pale knuckle, a highlight on the skin) is dropped unless it
        // joins the garment.
        var joined = new bool[result.Length];
        var queue = new Queue<int>();
        for (var i = 0; i < result.Length; i++)
            if (nearOutside[i] == 0 && result[i] >= 0.5f)
            {
                joined[i] = true;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            foreach (var next in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
            {
                if (next < 0 || joined[next] || result[next] < 0.5f) continue;
                joined[next] = true;
                queue.Enqueue(next);
            }
        }
        for (var i = 0; i < result.Length; i++)
            if (!joined[i] && result[i] >= 0.5f) result[i] = 0;

        // A little of the garment is only kept right at its edge; further off, on an arm say, it is a tint that
        // does not belong there.
        var edge = Dilate(joined.Select(value => value ? 1f : 0f).ToArray(), w, h, EdgeSoftWidth);
        for (var i = 0; i < result.Length; i++)
            if (!joined[i] && edge[i] == 0) result[i] = 0;

        // Chosen pixel by pixel, the edge follows the photo's noise and runs in steps. It is smoothed as a line:
        // blurred, then made sharp again around its middle, so it neither moves nor gets a soft, pale rim.
        var smooth = GaussianBlur(result, w, h, Math.Max(1.0, Math.Max(w, h) / 600.0));
        for (var i = 0; i < result.Length; i++)
            result[i] = (float)Math.Clamp((smooth[i] - 0.5) * EdgeSharpness + 0.5, 0, 1);
        return result;
    }

    // Where the clothes model is at least this sure of the garment it may overrule the person's matte.
    private const double SureOfGarment = 0.9;
    // How close to the garment's own color (Lab hue, and lightness) such a pixel has to be.
    private const double MendHue = 6;
    private const double MendLightness = 12;

    /// <summary>
    /// The person's matte with what it cut out of the garment put back. A white shirt against a white wall gives
    /// the matte nothing to go by there, and it can cut a notch into a shoulder; the clothes model, which sees the
    /// garment's shape, is trusted instead where it is sure and the photo there has the garment's color.
    /// </summary>
    /// <param name="pixels">The photo's pixels.</param>
    /// <param name="person">The person's matte (0..255).</param>
    /// <param name="upper">How likely each pixel is the garment, from the clothes model (0..255).</param>
    internal static byte[] MendPersonMatte(Rgba32[] pixels, byte[] person, byte[] upper)
    {
        // The garment's color, from where both are sure of it.
        var ls = new List<float>();
        var hueA = new List<float>();
        var hueB = new List<float>();
        for (var i = 0; i < pixels.Length; i += 7)
        {
            if (upper[i] < 245 || person[i] < 250) continue;
            var lab = Lab(pixels[i]);
            ls.Add(lab.L);
            hueA.Add(lab.A);
            hueB.Add(lab.B);
        }
        if (ls.Count < 30)
            return person;
        static float Median(List<float> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
        var (garmentL, garmentA, garmentB) = (Median(ls), Median(hueA), Median(hueB));

        var mended = (byte[])person.Clone();
        for (var i = 0; i < pixels.Length; i++)
        {
            var sure = (upper[i] / 255.0 - SureOfGarment) / (1 - SureOfGarment);
            if (sure <= 0 || person[i] == 255) continue;
            var color = Lab(pixels[i]);
            var hue = Math.Sqrt((color.A - garmentA) * (color.A - garmentA) + (color.B - garmentB) * (color.B - garmentB));
            if (hue > MendHue || Math.Abs(color.L - garmentL) > MendLightness) continue;
            mended[i] = Math.Max(person[i], ToByte(255 * Math.Min(1, sure)));
        }
        return mended;
    }

    private static void KeepNearest(Span<double> nearest, Span<int> at, double distance, int sample)
    {
        for (var k = 0; k < nearest.Length; k++)
        {
            if (distance >= nearest[k]) continue;
            for (var j = nearest.Length - 1; j > k; j--)
            {
                nearest[j] = nearest[j - 1];
                at[j] = at[j - 1];
            }
            nearest[k] = distance;
            at[k] = sample;
            return;
        }
    }

    // The average hue (Lab a and b) of the samples found.
    private static (double A, double B) MeanHue((float L, float A, float B)[] samples, Span<int> at)
    {
        double a = 0, b = 0;
        var count = 0;
        foreach (var k in at)
        {
            if (k < 0) continue;
            a += samples[k].A;
            b += samples[k].B;
            count++;
        }
        return count == 0 ? (0, 0) : (a / count, b / count);
    }

    private static double? MeanOfKnown(Span<double> nearest)
    {
        double sum = 0;
        var count = 0;
        foreach (var value in nearest)
            if (value < double.MaxValue)
            {
                sum += value;
                count++;
            }
        return count == 0 ? null : sum / count;
    }

    private static readonly float[] LinearLevel = Enumerable.Range(0, 256)
        .Select(v => (float)(v / 255.0 <= 0.04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4)))
        .ToArray();

    /// <summary>CIE Lab (D65) of an sRGB pixel: lightness 0..100 and two color axes, where distance follows how different colors look.</summary>
    internal static (float L, float A, float B) Lab(Rgba32 p)
    {
        double r = LinearLevel[p.R], g = LinearLevel[p.G], b = LinearLevel[p.B];
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        var fx = F((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047);
        var fy = F(0.2126 * r + 0.7152 * g + 0.0722 * b);
        var fz = F((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883);
        return ((float)(116 * fy - 16), (float)(500 * (fx - fy)), (float)(200 * (fy - fz)));
    }

    /// <summary>Drops subject islands smaller than <paramref name="minimumShare"/> of the image (specks of a textured background).</summary>
    internal static float[] RemoveSpecks(float[] subject, int w, int h, double minimumShare)
    {
        var label = new int[subject.Length];
        var result = (float[])subject.Clone();
        var minimum = (int)(subject.Length * minimumShare);
        var next = 0;
        var stack = new Stack<int>();
        var members = new List<int>();

        for (var start = 0; start < subject.Length; start++)
        {
            if (subject[start] < 0.5f || label[start] != 0) continue;
            next++;
            members.Clear();
            label[start] = next;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                members.Add(i);
                int x = i % w, y = i / w;
                foreach (var j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                {
                    if (j < 0 || subject[j] < 0.5f || label[j] != 0) continue;
                    label[j] = next;
                    stack.Push(j);
                }
            }

            if (members.Count < minimum)
                foreach (var i in members) result[i] = 0f;
        }

        return result;
    }

    internal static float[] Dilate(float[] mask, int w, int h, int radius) =>
        Threshold(BoxBlur(mask, w, h, radius), 0.001f);

    private static float[] Threshold(float[] values, float cutoff)
    {
        var result = new float[values.Length];
        for (var i = 0; i < values.Length; i++) result[i] = values[i] >= cutoff ? 1f : 0f;
        return result;
    }

    private static Rgba32 MedianBorderColor(Rgba32[] pixels, int w, int h)
    {
        var border = new List<Rgba32>(2 * (w + h));
        for (var x = 0; x < w; x++)
        {
            border.Add(pixels[x]);
            border.Add(pixels[(h - 1) * w + x]);
        }

        for (var y = 1; y < h - 1; y++)
        {
            border.Add(pixels[y * w]);
            border.Add(pixels[y * w + w - 1]);
        }

        static byte Median(IEnumerable<byte> values)
        {
            var sorted = values.Order().ToArray();
            return sorted[sorted.Length / 2];
        }

        return new Rgba32(Median(border.Select(p => p.R)), Median(border.Select(p => p.G)), Median(border.Select(p => p.B)));
    }

    internal static bool[] FloodFillFromBorder(bool[] allowed, int w, int h)
    {
        var visited = new bool[w * h];
        var queue = new Queue<int>();

        void Visit(int i)
        {
            if (visited[i] || !allowed[i]) return;
            visited[i] = true;
            queue.Enqueue(i);
        }

        for (var x = 0; x < w; x++)
        {
            Visit(x);
            Visit((h - 1) * w + x);
        }

        for (var y = 0; y < h; y++)
        {
            Visit(y * w);
            Visit(y * w + w - 1);
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (y > 0) Visit(i - w);
            if (y < h - 1) Visit(i + w);
        }

        return visited;
    }

    // Three box blurs approximate a Gaussian closely and cost O(pixels) regardless of sigma.
    internal static float[] GaussianBlur(float[] values, int w, int h, double sigma)
    {
        if (sigma < 0.5) return (float[])values.Clone();
        var radius = Math.Max(1, (int)Math.Round((Math.Sqrt(12 * sigma * sigma / 3 + 1) - 1) / 2));
        var result = values;
        for (var pass = 0; pass < 3; pass++) result = BoxBlur(result, w, h, radius);
        return result;
    }

    private static float[] BoxBlur(float[] values, int w, int h, int radius)
    {
        var horizontal = new float[values.Length];
        for (var y = 0; y < h; y++) BlurLine(values, horizontal, y * w, 1, w, radius);

        var result = new float[values.Length];
        for (var x = 0; x < w; x++) BlurLine(horizontal, result, x, w, h, radius);
        return result;
    }

    // Edge pixels are repeated past the border, so the mean near an edge is not pulled toward 0.
    private static void BlurLine(float[] source, float[] target, int start, int stride, int length, int radius)
    {
        float At(int index) => source[start + Math.Clamp(index, 0, length - 1) * stride];

        double sum = 0;
        for (var k = -radius; k <= radius; k++) sum += At(k);

        var window = 2 * radius + 1;
        for (var i = 0; i < length; i++)
        {
            target[start + i * stride] = (float)(sum / window);
            sum += At(i + radius + 1) - At(i - radius);
        }
    }
}
