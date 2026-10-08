using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed class VideoStoryboardPlanner
{
    public static readonly string[] ProductTypes = ["tshirt", "t-shirt", "t_shirt", "hoodie", "mug", "poster", "tote_bag", "phone_case"];
    public Result<Storyboard> Plan(Product product, IReadOnlyList<MockupImage> assets, GenerateVideoConfig config)
    {
        if (!ProductTypes.Contains(product.ProductType.ToLowerInvariant())) return Result.Failure<Storyboard>(Error.Validation("Unsupported product type."));
        var eligible = assets.Where(x => x.ProductId == product.Id && x.DeletedAt == null && x.ApprovalStatus == "approved" &&
            x.ApprovedRevision == x.MetadataRevision && !string.IsNullOrEmpty(x.ContentHash)).ToList();
        if (config.AssetSelection == "manual") eligible = eligible.Where(x => config.SelectedMockupIds!.Contains(x.Id)).ToList();
        if (eligible.Count == 0) return Result.Failure<Storyboard>(Error.Conflict("WaitingForInput", "Approve at least one mockup at its current revision."));
        if (eligible.Select(x => x.ArtworkGroupKey).Distinct().Count() != 1 || string.IsNullOrWhiteSpace(eligible[0].ArtworkGroupKey))
            return Result.Failure<Storyboard>(Error.Validation("All selected mockups must share an artwork group."));
        var templateVersion = config.TemplateVersion ?? 1;
        var output = VideoOutputFormats.Resolve(config.OutputFormat);
        var template = ResolveTemplate(config.Template, eligible, templateVersion);
        if (templateVersion >= 2 && template == "design_detail" &&
            !eligible.Any(x => x.Role == "ArtworkDetail" || WorkflowJson.Read<MockupRegions>(x.Regions).Detail != null))
            return Result.Failure<Storyboard>(Error.Validation("Design Detail requires an artwork-detail image or a marked detail region."));
        if (templateVersion >= 2 && template == "variant_showcase" &&
            eligible.Select(x => x.VariantKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() < 2)
            return Result.Failure<Storyboard>(Error.Validation("Variant Showcase requires at least two named variants."));
        var roles = template switch
        {
            "design_detail" => new[] { "Hero", "ArtworkDetail", "AlternativeAngle", "Lifestyle" },
            "variant_showcase" => new[] { "Hero", "Variant", "AlternativeAngle", "Lifestyle" },
            _ => new[] { "Hero", "AlternativeAngle", "Lifestyle", "ArtworkDetail" }
        };
        var preferred = eligible
            .OrderBy(x => template == "design_detail" && HasDetail(x) ? 0 : 1)
            .ThenBy(x => Array.IndexOf(roles, x.Role) < 0 ? 99 : Array.IndexOf(roles, x.Role))
            .ThenBy(x => x.VariantKey, StringComparer.Ordinal)
            .ThenByDescending(x => (long)x.MockupWidthPx * x.MockupHeightPx)
            .ThenBy(x => x.Id)
            .ToList();
        var ordered = templateVersion >= 2 && template == "variant_showcase"
            ? preferred.GroupBy(x => string.IsNullOrWhiteSpace(x.VariantKey) ? x.Id.ToString() : x.VariantKey!, StringComparer.Ordinal)
                .Select(x => x.First()).Concat(preferred).DistinctBy(x => x.Id).Take(4).ToList()
            : preferred.Take(4).ToList();
        if (config.SceneOrder is { Count: > 0 })
        {
            if (config.SceneOrder.Any(id => eligible.All(x => x.Id != id))) return Result.Failure<Storyboard>(Error.Validation("Scene order contains an unavailable mockup."));
            ordered = config.SceneOrder.Select(id => eligible.Single(x => x.Id == id)).ToList();
        }
        if (ordered.Count == 1) ordered.Add(ordered[0]);
        var totalFrames = config.DurationSeconds * 30;
        var scenes = ordered.Select((asset, index) => CreateScene(asset, product.ProductType, config, index,
            totalFrames / ordered.Count + (index < totalFrames % ordered.Count ? 1 : 0))).ToArray();
        if (templateVersion >= 2 && ordered.Count == 2 && ordered[0].Id == ordered[1].Id &&
            scenes[0].Motion == scenes[1].Motion && config.SceneMotionPresets is not { Count: > 0 })
            scenes[1] = scenes[1] with { Motion = scenes[0].Motion == "static" ? "contain_gentle" : "static" };
        var rendererVersion = templateVersion >= 2 ? "standard-v2" : "standard-v1";
        var fingerprint = WorkflowJson.Hash(WorkflowJson.Write(new { product.Id, config, template, templateVersion, rendererVersion, scenes }));
        return Result.Success(new Storyboard(template, templateVersion, config.DurationSeconds, product.ProductType, scenes, fingerprint,
            output.Format, output.Width, output.Height, output.AspectRatio));
    }

    public ScenePlan CreateScene(MockupImage asset, string productType, GenerateVideoConfig config, int index, int frames)
    {
        var regions = WorkflowJson.Read<MockupRegions>(asset.Regions);
        var focus = regions.FocalPoint ?? new Point();
        var protectedRegion = asset.Role == "ArtworkDetail" ? regions.Detail ?? regions.Artwork ?? regions.Product : regions.Product;
        var warnings = new List<string>();
        var templateVersion = config.TemplateVersion ?? 1;
        var output = VideoOutputFormats.Resolve(config.OutputFormat);
        var full = new Region(0, 0, 1, 1);
        var crop = full;
        var end = full;
        var motion = config.SceneMotionPresets is { } presets && index < presets.Count && presets[index] is { } selected
            ? selected : config.StandardOptions?.MotionPreset ?? "gentle";
        if (motion == "varied") motion = new[] { "zoom", "pan_left", "diagonal_up_right", "zoom_out" }[index % 4];
        var sourceRatio = (double)asset.MockupWidthPx / Math.Max(1, asset.MockupHeightPx);
        var outputRatio = (double)output.Width / output.Height;
        var height = sourceRatio < outputRatio ? sourceRatio / outputRatio : 1;
        var width = sourceRatio > outputRatio ? outputRatio / sourceRatio : 1;
        var candidate = new Region(Math.Clamp(focus.X - width / 2, 0, 1 - width), Math.Clamp(focus.Y - height / 2, 0, 1 - height), width, height);
        if (motion == "contain_gentle")
        {
            crop = full;
            end = full;
        }
        else if (protectedRegion == null || !Contains(candidate, protectedRegion))
        {
            motion = templateVersion >= 2 && motion != "static" ? "contain_gentle" : "static";
            warnings.Add(protectedRegion == null
                ? templateVersion >= 2 && motion == "contain_gentle"
                    ? "Product region is not marked. Gentle full-image motion preserves the complete image."
                    : "Product region is not marked. Static contain framing preserves the complete image."
                : templateVersion >= 2 && motion == "contain_gentle"
                    ? "Vertical crop would clip the protected region. Gentle full-image motion is used."
                    : "Vertical crop would clip the protected region. Static contain framing is used.");
        }
        else
        {
            crop = candidate;
            end = crop;
            if (motion != "static")
            {
                (crop, end) = motion switch
                {
                    "gentle" => (candidate, Inset(candidate, productType is "mug" or "phone_case" ? .985 : .97)),
                    "zoom" => (candidate, Inset(candidate, .92)),
                    "zoom_out" => (Inset(candidate, .92), candidate),
                    "pan" => Pan(candidate, 1, 0),
                    "pan_left" => Pan(candidate, -1, 0),
                    "pan_up" => Pan(candidate, 0, -1),
                    "pan_down" => Pan(candidate, 0, 1),
                    "diagonal_up_right" => Pan(candidate, 1, -1),
                    "diagonal_up_left" => Pan(candidate, -1, -1),
                    "diagonal_down_right" => Pan(candidate, 1, 1),
                    "diagonal_down_left" => Pan(candidate, -1, 1),
                    _ => (candidate, candidate)
                };
                if (!Contains(crop, protectedRegion) || !Contains(end, protectedRegion))
                {
                    crop = full; end = full; motion = templateVersion >= 2 ? "contain_gentle" : "static";
                    warnings.Add("Motion reduced to preserve product/artwork bounds.");
                }
            }
        }
        if (asset.MockupWidthPx < output.Width / 2 || asset.MockupHeightPx < output.Height / 2)
            warnings.Add($"Low-resolution source may appear soft at {output.Width} × {output.Height}.");
        return new(asset.Id, asset.MetadataRevision, asset.ContentHash!, asset.Role, index, frames, crop, end, motion,
            config.StandardOptions?.Transition ?? "fade", config.TextOverlay, "standard", warnings);
    }

    private static string ResolveTemplate(string requested, IReadOnlyList<MockupImage> eligible, int templateVersion)
    {
        if (requested != "auto") return requested;
        if (eligible.Select(x => x.VariantKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() > 1)
            return "variant_showcase";
        if (eligible.Any(x => x.Role == "ArtworkDetail" || templateVersion >= 2 && WorkflowJson.Read<MockupRegions>(x.Regions).Detail != null))
            return "design_detail";
        return "product_showcase";
    }
    private static bool HasDetail(MockupImage asset) => asset.Role == "ArtworkDetail" ||
        WorkflowJson.Read<MockupRegions>(asset.Regions).Detail != null;
    private static Region Inset(Region bounds, double scale) => new(
        bounds.X + bounds.Width * (1 - scale) / 2,
        bounds.Y + bounds.Height * (1 - scale) / 2,
        bounds.Width * scale,
        bounds.Height * scale);

    private static (Region Start, Region End) Pan(Region bounds, int horizontal, int vertical)
    {
        var width = bounds.Width * .9;
        var height = bounds.Height * .9;
        var horizontalTravel = bounds.Width - width;
        var verticalTravel = bounds.Height - height;
        var startX = bounds.X + (horizontal > 0 ? 0 : horizontal < 0 ? horizontalTravel : horizontalTravel / 2);
        var startY = bounds.Y + (vertical > 0 ? 0 : vertical < 0 ? verticalTravel : verticalTravel / 2);
        var endX = bounds.X + (horizontal > 0 ? horizontalTravel : horizontal < 0 ? 0 : horizontalTravel / 2);
        var endY = bounds.Y + (vertical > 0 ? verticalTravel : vertical < 0 ? 0 : verticalTravel / 2);
        return (new(startX, startY, width, height), new(endX, endY, width, height));
    }
    private static bool Contains(Region outer, Region inner) => inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width + 1e-9 && inner.Y + inner.Height <= outer.Y + outer.Height + 1e-9;
}
