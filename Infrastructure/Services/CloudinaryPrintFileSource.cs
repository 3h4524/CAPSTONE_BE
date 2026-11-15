using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Resizes a Cloudinary-stored design on delivery: a transformation spliced in after <c>/upload/</c>. With
/// <see cref="PrintifyOptions.AiUpscale"/> a design much smaller than its print is enlarged with Cloudinary's AI
/// upscale first (it adds detail; plain scaling only adds pixels), which counts against the Cloudinary plan.
/// </summary>
public sealed class CloudinaryPrintFileSource(IOptions<PrintifyOptions> options) : IPrintFileSource
{
    private const string UploadMarker = "/upload/";

    /// <summary>Cloudinary's AI upscale takes images up to about this many pixels.</summary>
    internal const long AiUpscaleMaxInputPixels = 4_200_000;

    /// <summary>Kept under Cloudinary's largest delivered image, so a big print area never makes the file fail.</summary>
    internal const long MaxOutputPixels = 24_000_000;

    public string? Resized(string imageUrl, int sourceWidthPx, int sourceHeightPx, int widthPx)
    {
        var marker = imageUrl.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (marker < 0 || sourceWidthPx <= 0 || sourceHeightPx <= 0 || widthPx <= 0)
            return null;

        var limit = (int)Math.Floor(Math.Sqrt(MaxOutputPixels * (double)sourceWidthPx / sourceHeightPx));
        var width = Math.Min(widthPx, limit);
        if (width <= sourceWidthPx)
            return null;

        var steps = new List<string>();
        if (options.Value.AiUpscale && width >= sourceWidthPx * 1.25 && (long)sourceWidthPx * sourceHeightPx <= AiUpscaleMaxInputPixels)
            steps.Add("e_upscale");
        steps.Add($"c_scale,w_{width}");
        // PNG keeps a transparent background transparent.
        steps.Add("f_png");
        var at = marker + UploadMarker.Length;
        return imageUrl[..at] + string.Join('/', steps) + "/" + imageUrl[at..];
    }
}
