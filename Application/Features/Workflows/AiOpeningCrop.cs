using System.Globalization;
using APCS.Application.Abstractions.Storage;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// The opening crop of a Product On Model scene whose template looks at the product up close: Fabric Detail Close-Up
/// always, Brand Focus only when the product is small in the photo. It is cut from the approved image itself, so the
/// print keeps its approved pixels, around the garment the clothes model finds, and in the video's own aspect ratio,
/// so cutting the frame to that ratio afterwards never removes part of it. Whenever no good crop exists the scene
/// opens on the whole image, as before.
/// </summary>
public static class AiOpeningCrop
{
    public const string FabricDetail = "fabric_detail";
    public const string BrandFocus = "brand_focus";

    /// <summary>The shortest side, in source pixels, worth sending: anything smaller looks soft at 768P.</summary>
    public const int MinimumSidePx = 512;

    /// <summary>Brand Focus is cropped only when the garment covers less than this share of the photo.</summary>
    public const double BrandFocusMaximumGarmentShare = 0.3;

    /// <summary>A crop covering more than this share of the photo is barely closer, so the photo is sent whole.</summary>
    private const double MaximumCropShare = 0.85;

    private const string ProductOnModel = "lifestyle_in_use";

    /// <summary>Whether a Product On Model subtemplate opens on a crop.</summary>
    public static bool Applies(string? subtemplateCode) => subtemplateCode is FabricDetail or BrandFocus;

    /// <summary>The garment to look for on a product type, or null for a product a person neither wears nor carries.</summary>
    public static WornGarment? GarmentOf(string? productType) => productType?.Trim().ToLowerInvariant() switch
    {
        "tshirt" or "t-shirt" or "t_shirt" or "hoodie" => WornGarment.UpperBody,
        "tote_bag" => WornGarment.Bag,
        _ => null
    };

    /// <summary>
    /// The crop (shares of the image) a scene of <paramref name="subtemplateCode"/> opens on, around
    /// <paramref name="garment"/> and in <paramref name="aspectRatio"/>; null when the subtemplate is not cropped,
    /// the product is already large enough (Brand Focus), or the crop would be too small to stay sharp.
    /// </summary>
    public static Region? For(string? subtemplateCode, Region garment, int imageWidth, int imageHeight, string aspectRatio)
    {
        if (!Applies(subtemplateCode) || imageWidth <= 0 || imageHeight <= 0 || !IsBox(garment)) return null;
        if (Ratio(aspectRatio) is not { } ratio) return null;

        double garmentX = garment.X * imageWidth, garmentY = garment.Y * imageHeight;
        double garmentWidth = garment.Width * imageWidth, garmentHeight = garment.Height * imageHeight;
        double width, height, centreY;
        if (subtemplateCode == FabricDetail)
        {
            // The upper chest, where the collar and a front print sit: about one and a half times the print.
            width = garmentWidth * 0.7;
            height = garmentHeight * 0.5;
            centreY = garmentY + garmentHeight * 0.3;
        }
        else
        {
            if (garment.Width * garment.Height >= BrandFocusMaximumGarmentShare) return null;
            // The whole garment with the shoulders and neck above it: a medium-close shot with the design in the middle.
            width = garmentWidth * 1.35;
            height = garmentHeight * 1.25;
            centreY = garmentY + garmentHeight * 0.42;
        }

        // Widened, never narrowed, to the video's ratio.
        if (width / height < ratio) width = height * ratio;
        else height = width / ratio;
        var fit = Math.Min(1d, Math.Min(imageWidth / width, imageHeight / height));
        width *= fit;
        height *= fit;
        if (Math.Min(width, height) < MinimumSidePx || width * height > MaximumCropShare * imageWidth * imageHeight) return null;

        var x = Math.Clamp(garmentX + garmentWidth / 2 - width / 2, 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp(centreY - height / 2, 0, Math.Max(0, imageHeight - height));
        // Rounded down, so the same garment always gives the same crop (it is part of the scene's fingerprint) and the
        // crop never reaches past the image.
        return new Region(Floor(x / imageWidth), Floor(y / imageHeight), Floor(width / imageWidth), Floor(height / imageHeight));
    }

    /// <summary>
    /// The garment boxes the planner needs: one for each image an AI scene of Fabric Detail or Brand Focus opens on
    /// directly. Null when nothing needs one, so planning does not wait for the clothes model.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, Region>?> LocateGarmentsAsync(IGarmentLocator? locator, Product product,
        GenerateVideoConfig config, IReadOnlyList<MockupImage> sources, CancellationToken cancellationToken)
    {
        if (locator is null || GarmentOf(product.ProductType) is not { } garment || config.AiScenes is not { Count: > 0 } scenes)
            return null;
        var ids = scenes
            .Where(scene => scene.TemplateCode == ProductOnModel && Applies(scene.SubtemplateCode) && scene.StartFrameMode != "studio"
                && string.IsNullOrWhiteSpace(scene.CustomPrompt) && scene.MockupId is not null)
            .Select(scene => scene.MockupId!.Value)
            .ToHashSet();
        if (ids.Count == 0) return null;

        var boxes = new Dictionary<Guid, Region>();
        foreach (var image in sources.Where(source => ids.Contains(source.Id)).DistinctBy(source => source.Id))
        {
            if (await locator.LocateAsync(image, garment, cancellationToken) is { } box) boxes[image.Id] = box;
        }
        return boxes;
    }

    /// <summary>Whether the planner may crop this scene's opening image: the template prompts were written for it.</summary>
    internal static bool PlannerApplies(AiVideoTemplateDefinition definition, AiVideoSceneOptions settings, bool studioFrame) =>
        !studioFrame && definition.Code == ProductOnModel && Applies(settings.SubtemplateCode) && string.IsNullOrWhiteSpace(settings.CustomPrompt);

    private static bool IsBox(Region region) =>
        double.IsFinite(region.X + region.Y + region.Width + region.Height) &&
        region is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 } && region.X + region.Width <= 1.0001 && region.Y + region.Height <= 1.0001;

    private static double? Ratio(string aspectRatio)
    {
        var parts = aspectRatio.Split(':');
        return parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
            double.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) &&
            width > 0 && height > 0
                ? width / height
                : null;
    }

    private static double Floor(double value) => Math.Floor(value * 10_000) / 10_000;
}
