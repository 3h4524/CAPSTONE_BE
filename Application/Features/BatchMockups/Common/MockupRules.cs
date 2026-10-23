using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Storage;
using APCS.Domain.Entities;

namespace APCS.Application.Features.BatchMockups.Common;

/// <summary>Shared limits, and print-area JSON parsing, for mock-up templates.</summary>
public static class MockupRules
{
    public const int MinimumSelection = 1;
    public const int MaximumSelection = 5;
    public const string ConfigKey = "mockupTemplateIds";

    public const long MaximumBaseImageBytes = 10 * 1024 * 1024;
    public const int MaximumNameLength = 120;

    /// <summary>
    /// Parses a <c>mockup_templates.print_area_config</c> JSON string (e.g.
    /// <c>{"x":820,"y":640,"width":900,"height":1100,"unit":"px"}</c>) into a position. Returns
    /// <see langword="null"/> when the stored value is missing or malformed rather than throwing —
    /// callers decide how to react to a broken template.
    /// </summary>
    public static MockupPosition? ParsePrintArea(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            var x = (int?)node?["x"];
            var y = (int?)node?["y"];
            var width = (int?)node?["width"];
            var height = (int?)node?["height"];
            return x is null || y is null || width is null || height is null
                ? null
                : new MockupPosition(x.Value, y.Value, width.Value, height.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializePrintArea(MockupPosition position) =>
        JsonSerializer.Serialize(new { x = position.X, y = position.Y, width = position.Width, height = position.Height, unit = "px" });

    public static string BaseImageKey(Guid templateId) => $"mockup-templates/{templateId:N}";

    /// <summary>
    /// Where a photo that replaces the template's current one is stored. Each replacement gets its own key:
    /// a mock-up is a URL that composites onto the photo it was made from, so overwriting that photo would
    /// change every mock-up already made.
    /// </summary>
    public static string ReplacementImageKey(Guid templateId, long version) => $"{BaseImageKey(templateId)}-{version}-photo";

    /// <summary>Key prefix of one generation of helper images; a new version gives new URLs, so nothing stale is served from cache.</summary>
    public static string HelperKeyPrefix(Guid templateId, long version) => $"{BaseImageKey(templateId)}-{version}";
    public static string DisplacementMapKey(string helperKeyPrefix) => $"{helperKeyPrefix}-displace";
    public static string GarmentMaskKey(string helperKeyPrefix) => $"{helperKeyPrefix}-mask";
    public static string GarmentMaskKey(MockupTemplate template) => GarmentMaskKey(HelperKeyPrefix(template.Id, template.PrintMapsVersion ?? 0));
    public static string CutMaskKey(string helperKeyPrefix) => $"{helperKeyPrefix}-cut";
    public static string CutMaskKey(MockupTemplate template) => CutMaskKey(HelperKeyPrefix(template.Id, template.PrintMapsVersion ?? 0));

    /// <summary>
    /// Per template and garment color, the '#RRGGBB' background the mock-ups of a job are put on; a mock-up
    /// without one keeps its photo's background.
    /// </summary>
    public const string TemplateBackgroundsConfigKey = "mockupTemplateBackgrounds";

    /// <summary>The key of the background for every garment color that has none of its own, the photo's own color included.</summary>
    public const string DefaultBackgroundKey = "default";

    /// <summary>The background chosen for a template's mock-up in <paramref name="garmentColor"/>, or <see langword="null"/> to keep the photo's.</summary>
    public static string? ResolveBackground(IReadOnlyDictionary<string, string>? byColor, string? garmentColor) =>
        byColor is null ? null
        : garmentColor is not null && byColor.TryGetValue(garmentColor.ToUpperInvariant(), out var own) ? own
        : byColor.GetValueOrDefault(DefaultBackgroundKey);

    /// <summary>The mock-ups of a job made apart from its shared settings, by slot key (see <see cref="SlotKey"/>).</summary>
    public const string OverridesConfigKey = "mockupOverrides";

    public const string SlotKeyPattern = "^[0-9a-f]{32}\\.[0-9a-f]{32}\\.([0-9A-F]{6}|own)$";

    /// <summary>The most mock-ups one save may change, enough for a large job's every mock-up.</summary>
    public const int MaxOverridesPerSave = 500;

    /// <summary>
    /// Names one mock-up that a job's shared settings make: a design on one selected template in one garment color
    /// (or in the template's own). A mock-up made its own way is kept under this name, so the change applies again
    /// every time the job's mock-ups are made. Safe in a URL.
    /// </summary>
    public static string SlotKey(Guid designImageId, Guid templateId, string? garmentColor) =>
        $"{designImageId:N}.{templateId:N}.{(garmentColor is null ? "own" : garmentColor.TrimStart('#').ToUpperInvariant())}";

    /// <summary>The design image and template a slot key names, or false for anything else.</summary>
    public static bool TryReadSlotKey(string? slotKey, out Guid designImageId, out Guid templateId)
    {
        designImageId = Guid.Empty;
        templateId = Guid.Empty;
        if (slotKey is null || !System.Text.RegularExpressions.Regex.IsMatch(slotKey, SlotKeyPattern))
            return false;
        var parts = slotKey.Split('.');
        return Guid.TryParseExact(parts[0], "N", out designImageId) && Guid.TryParseExact(parts[1], "N", out templateId);
    }

    public const string GarmentColorPattern = "^#[0-9A-Fa-f]{6}$";
    public const string GarmentColorsConfigKey = "mockupGarmentColors";
    public const int MaximumGarmentColors = 5;

    // Multiplying a color only darkens, so recoloring needs a light garment that the mask
    // separated cleanly from a plain background.
    public const double MinimumRecolorLuminance = 0.7;
    public const double MinimumGarmentCoverage = 0.1;
    public const double MaximumGarmentCoverage = 0.9;

    /// <summary>Whether the mask separated a garment from the background at all.</summary>
    public static bool IsSeparated(GarmentMaskStats mask) =>
        mask.Coverage is >= MinimumGarmentCoverage and <= MaximumGarmentCoverage;

    /// <summary>Why a photo with this mask can't be recolored, or <see langword="null"/> when it can.</summary>
    public static string? RecolorProblem(GarmentMaskStats mask) =>
        !IsSeparated(mask) ? "the garment could not be separated from the background."
        : mask.Luminance < MinimumRecolorLuminance ? "the garment is too dark."
        : null;

    /// <summary>Whether the template's helper images were made from its current base photo.</summary>
    public static bool HasCurrentMaps(MockupTemplate template) =>
        template.PrintMapsVersion is not null
        && !string.IsNullOrEmpty(template.PrintMapsSourceUrl) && template.PrintMapsSourceUrl == template.BaseImageUrl;

    /// <summary>
    /// The realism layers for compositing <paramref name="designImage"/> onto <paramref name="template"/>,
    /// with the garment recolored when <paramref name="garmentColor"/> is given and lifted onto
    /// <paramref name="backgroundColor"/> when that is. The caller makes sure the cut mask exists first.
    /// </summary>
    public static MockupLayers Layers(MockupTemplate template, DesignImage designImage, string? garmentColor, string? backgroundColor = null)
    {
        var mapsReady = HasCurrentMaps(template);
        var recolor = mapsReady && template.AllowRecolor && garmentColor is not null;
        var prefix = mapsReady ? HelperKeyPrefix(template.Id, template.PrintMapsVersion!.Value) : null;
        var background = prefix is not null && backgroundColor is not null;
        return new MockupLayers(
            designImage.ImageWidthPx,
            designImage.ImageHeightPx,
            prefix is null ? null : DisplacementMapKey(prefix),
            prefix is null ? null : GarmentMaskKey(prefix),
            recolor ? garmentColor!.ToUpperInvariant() : null,
            MultiplyDesign: mapsReady && template.GarmentIsLight && !recolor,
            BasePhotoShortSidePx: Math.Min(template.OutputWidthPx, template.OutputHeightPx),
            CutMaskKey: background ? CutMaskKey(prefix!) : null,
            BackgroundColor: background ? backgroundColor!.ToUpperInvariant() : null,
            BasePhotoWidthPx: template.OutputWidthPx,
            BasePhotoHeightPx: template.OutputHeightPx);
    }
}
