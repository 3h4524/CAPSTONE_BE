using APCS.Application.Abstractions.Storage;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Builds a Cloudinary "on-the-fly" overlay transformation URL: splices a transformation component
/// into an existing delivery URL right after "/upload/", combining two already-uploaded public IDs
/// (the mock-up base photo and the design image) without any new upload/HTTP call.
/// </summary>
/// <remarks>
/// This is built as plain string formatting of Cloudinary's classical transformation syntax
/// (<c>l_&lt;overlay&gt;,g_north_west,x_..,y_..,w_..,h_..,c_fit,fl_layer_apply</c>) rather than through
/// the CloudinaryDotNet SDK's fluent <c>Transformation</c>/<c>Layer</c> builder, so the exact output is
/// deterministic and easy to unit test.
/// </remarks>
public sealed class CloudinaryMockupCompositor(IOptions<CloudinaryOptions> options) : IMockupCompositor
{
    private const string UploadMarker = "/upload/";

    /// <summary>
    /// Largest shift the displacement map can cause (map value 0 or 255; 128 is no shift), as a share
    /// of the design's shorter side so the bend looks the same on small and large photos.
    /// </summary>
    internal const double DisplacementStrengthRatio = 0.05;

    private readonly CloudinaryOptions _options = options.Value;

    public string BuildCompositeUrl(string baseImageUrl, string overlayStorageKey, MockupPosition position, MockupLayers? layers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseImageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayStorageKey);
        ArgumentNullException.ThrowIfNull(position);

        var markerIndex = baseImageUrl.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException(
                $"'{baseImageUrl}' does not look like a Cloudinary delivery URL (missing '{UploadMarker}').");
        }

        // The overlay's real Cloudinary public id includes the same root folder every upload goes
        // through (see CloudinaryPaths.BuildPublicId) — a bare storage key alone 404s. Cloudinary's
        // classical transformation syntax then requires "/" to be encoded as ":" once embedded inside
        // a transformation component (it would otherwise be read as another path segment).
        var overlayToken = LayerToken(overlayStorageKey);
        var components = new List<string>();

        // Recolor: the garment is first painted a flat color; its shading is multiplied back on
        // after the design, so garment and print share the same folds and texture.
        string? recolorMaskToken = null;
        if (layers is { GarmentMaskKey: { } maskKey, GarmentColor: { } color })
        {
            recolorMaskToken = LayerToken(maskKey);
            components.Add($"l_{recolorMaskToken}");
            components.Add($"e_colorize:100,co_rgb:{color.TrimStart('#').ToUpperInvariant()}");
            components.Add("fl_layer_apply,g_north_west,x_0,y_0");
        }

        if (FitDesign(position, layers) is { } fitted)
        {
            // Scaled to its exact fitted size so the displacement map, cropped to the same
            // rectangle, lines up with it pixel for pixel.
            components.Add($"l_{overlayToken}");
            components.Add($"c_scale,w_{fitted.Width},h_{fitted.Height}");
            if (layers!.DisplacementMapKey is { } displacementKey)
            {
                components.Add($"l_{LayerToken(displacementKey)}");
                components.Add($"c_crop,g_north_west,x_{fitted.X},y_{fitted.Y},w_{fitted.Width},h_{fitted.Height}");
                var strength = Math.Max(2, (int)Math.Round(Math.Min(fitted.Width, fitted.Height) * DisplacementStrengthRatio));
                components.Add($"e_displace,fl_layer_apply,x_{strength},y_{strength}");
            }

            var blend = layers.MultiplyDesign ? "e_multiply," : "";
            components.Add($"fl_layer_apply,{blend}g_north_west,x_{fitted.X},y_{fitted.Y}");
        }
        else
        {
            components.Add(string.Join(',',
            [
                $"l_{overlayToken}",
                "g_north_west",
                $"x_{position.X}",
                $"y_{position.Y}",
                $"w_{position.Width}",
                $"h_{position.Height}",
                "c_fit",
                "fl_layer_apply"
            ]));
        }

        if (recolorMaskToken is not null)
        {
            components.Add($"l_{recolorMaskToken}");
            components.Add("fl_layer_apply,e_multiply,g_north_west,x_0,y_0");
        }

        return baseImageUrl.Insert(markerIndex + UploadMarker.Length, string.Join('/', components) + "/");
    }

    /// <summary>
    /// The design's rectangle after fitting it inside the print area and centering it, or
    /// <see langword="null"/> when its size is unknown.
    /// </summary>
    internal static MockupPosition? FitDesign(MockupPosition area, MockupLayers? layers)
    {
        if (layers is not { DesignWidthPx: > 0 and var designWidth, DesignHeightPx: > 0 and var designHeight })
            return null;

        var scale = Math.Min((double)area.Width / designWidth, (double)area.Height / designHeight);
        var width = Math.Clamp((int)Math.Round(designWidth * scale), 1, area.Width);
        var height = Math.Clamp((int)Math.Round(designHeight * scale), 1, area.Height);
        return new MockupPosition(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    public string BuildAssetUrl(string baseImageUrl, string storageKey)
    {
        var markerIndex = baseImageUrl.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException($"'{baseImageUrl}' does not look like a Cloudinary delivery URL (missing '{UploadMarker}').");

        return baseImageUrl[..(markerIndex + UploadMarker.Length)] + CloudinaryPaths.BuildPublicId(_options.Folder, storageKey) + ".png";
    }

    private string LayerToken(string storageKey) =>
        CloudinaryPaths.BuildPublicId(_options.Folder, storageKey).Replace('/', ':');
}
