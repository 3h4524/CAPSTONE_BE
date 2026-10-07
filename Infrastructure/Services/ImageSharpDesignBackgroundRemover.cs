using APCS.Application.Abstractions.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static APCS.Infrastructure.Services.ImageMaskOps;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Makes the plain backdrop around a generated design transparent, so only the artwork is printed
/// on the garment. Areas enclosed by the artwork are kept.
/// </summary>
public sealed class ImageSharpDesignBackgroundRemover : IDesignBackgroundRemover
{
    // Islands smaller than this share of the image are leftovers of a textured backdrop.
    internal const double MinimumIslandShare = 0.001;

    // Outside this range the "backdrop" found is the artwork itself or almost nothing; keep the image as is.
    internal const double MinimumRemovedShare = 0.02;
    internal const double MaximumRemovedShare = 0.95;

    public byte[]? RemoveBackground(byte[] image)
    {
        using var source = Image.Load<Rgba32>(image);
        var (width, height) = (source.Width, source.Height);
        using var working = ScaledCopy(source, MaskWorkingMaxSide);
        var (w, h) = (working.Width, working.Height);

        var subject = RemoveSpecks(FindSubject(Pixels(working), w, h), w, h, MinimumIslandShare);
        var removed = 1 - (double)subject.Average();
        if (removed is < MinimumRemovedShare or > MaximumRemovedShare)
            return null;

        var alpha = UpscaleAlpha(GaussianBlur(subject, w, h, 0.7), w, h, width, height);
        var pixels = Pixels(source);
        using var output = new Image<Rgba32>(width, height);
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            output[i % width, i / width] = new Rgba32(p.R, p.G, p.B, (byte)Math.Min(p.A, alpha[i]));
        }

        return EncodePng(output);
    }
}
