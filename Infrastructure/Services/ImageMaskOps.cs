using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
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
