using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Dtos.Response;
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

    // ---- Model photos: a person wearing the garment ----

    /// <summary>A template whose photo is the garment alone; the only kind a batch's mock-ups are made on.</summary>
    public const string GarmentKind = "garment";

    /// <summary>A template whose photo is a person wearing a plain, light garment, which an existing mock-up dresses.</summary>
    public const string ModelKind = "model";

    /// <summary>
    /// A shirt is a small part of a full-length photo of a person, so a model photo's garment may cover far less
    /// of it than a product photo's. Below this the selection is a slip of the brush rather than a garment.
    /// </summary>
    public const double MinimumModelGarmentCoverage = 0.02;

    public static bool IsModelPhoto(MockupTemplate template) => template.Kind == ModelKind;

    /// <summary>
    /// Why the garment selected on a model photo can't be used, or <see langword="null"/> when it can. It is
    /// always recolored, so it must be light.
    /// </summary>
    public static string? ModelPhotoProblem(GarmentMaskStats mask) =>
        mask.Coverage < MinimumModelGarmentCoverage ? "select more of the garment: the selection covers too little of the photo."
        : mask.Coverage > MaximumGarmentCoverage ? "select only the garment: the selection covers nearly the whole photo."
        : mask.Luminance < MinimumRecolorLuminance ? "the garment is too dark. Use a photo of a white or light garment."
        : null;

    /// <summary>
    /// Where a design goes on a model photo so that it sits on the garment as it does on a mock-up: the print area
    /// the mock-up was made with, taken relative to its template's own print area, laid onto the model photo's.
    /// The model photo's print area covers the same part of the garment as the template's does.
    /// </summary>
    /// <param name="used">The print area the mock-up was made with.</param>
    /// <param name="templateArea">The mock-up template's own print area.</param>
    /// <param name="modelArea">The model photo's print area.</param>
    /// <param name="modelWidth">The model photo's width; the result stays inside it.</param>
    /// <param name="modelHeight">The model photo's height.</param>
    public static MockupPosition MapPlacement(MockupPosition used, MockupPosition templateArea, MockupPosition modelArea, int modelWidth, int modelHeight)
    {
        var scaleX = (double)modelArea.Width / templateArea.Width;
        var scaleY = (double)modelArea.Height / templateArea.Height;
        return Within(
            modelArea.X + (used.X - templateArea.X) * scaleX,
            modelArea.Y + (used.Y - templateArea.Y) * scaleY,
            modelArea.X + (used.X + used.Width - templateArea.X) * scaleX,
            modelArea.Y + (used.Y + used.Height - templateArea.Y) * scaleY,
            modelWidth, modelHeight);
    }

    /// <summary>
    /// Where a design goes on a model photo, from the garments themselves: the print area the mock-up was made with
    /// keeps its size relative to the width of the garment's body, its distance below the garment's top (collar and
    /// shoulders) in those widths, and its offset from the body's center. It is scaled the same both ways, so the
    /// design keeps its shape. Needs no print area on the model photo. A worn garment wraps around the body, so the
    /// width seen of it is taken as <see cref="WornWidthShare"/> of the width it has laid flat on the template.
    /// </summary>
    /// <param name="used">The print area the mock-up was made with, in the template photo's pixels.</param>
    /// <param name="template">The garment measured on the template photo.</param>
    /// <param name="templateWidth">The template photo's width.</param>
    /// <param name="templateHeight">The template photo's height.</param>
    /// <param name="model">The garment measured on the model photo.</param>
    /// <param name="modelWidth">The model photo's width; the result stays inside it.</param>
    /// <param name="modelHeight">The model photo's height.</param>
    public static MockupPosition MapPlacementByGarment(MockupPosition used, GarmentMeasurement template, int templateWidth, int templateHeight,
        GarmentMeasurement model, int modelWidth, int modelHeight)
    {
        var scale = model.BodyWidth * modelWidth / (template.BodyWidth * templateWidth * WornWidthShare);
        var templateCenter = template.CenterX * templateWidth;
        var templateTop = template.Top * templateHeight;
        var modelCenter = model.CenterX * modelWidth;
        var modelTop = model.Top * modelHeight;
        var x = modelCenter + (used.X - templateCenter) * scale;
        var y = modelTop + (used.Y - templateTop) * scale;
        return Within(x, y, x + used.Width * scale, y + used.Height * scale, modelWidth, modelHeight);
    }

    /// <summary>
    /// The share of a garment's flat width seen from the front when it is worn; the rest wraps around the body. A
    /// design keeps the size it is printed at, so on a person it covers more of the width seen than on a flat lay.
    /// </summary>
    public const double WornWidthShare = 0.75;

    // A rectangle rounded to pixels and kept inside a photo, at least one pixel wide and high.
    private static MockupPosition Within(double left, double top, double right, double bottom, int width, int height)
    {
        var x = Math.Clamp((int)Math.Round(left), 0, Math.Max(0, width - 1));
        var y = Math.Clamp((int)Math.Round(top), 0, Math.Max(0, height - 1));
        var r = Math.Clamp((int)Math.Round(right), x + 1, Math.Max(x + 1, width));
        var b = Math.Clamp((int)Math.Round(bottom), y + 1, Math.Max(y + 1, height));
        return new MockupPosition(x, y, r - x, b - y);
    }

    /// <summary>
    /// A model photo's print area, marked as followed automatically: a design is placed by measuring the garments
    /// (<see cref="MapPlacementByGarment"/>), and the print area itself is only the fallback.
    /// </summary>
    public static string SerializePrintArea(MockupPosition position, bool automatic) =>
        automatic
            ? JsonSerializer.Serialize(new { x = position.X, y = position.Y, width = position.Width, height = position.Height, unit = "px", auto = true })
            : SerializePrintArea(position);

    /// <summary>
    /// A model photo's print area marked by its four corners (top-left, top-right, bottom-right, bottom-left), for
    /// a chest that is not square to the camera. The rectangle kept with them is the box around them.
    /// </summary>
    public static string SerializePrintQuad(IReadOnlyList<MockupPoint> quad)
    {
        var box = QuadBounds(quad);
        return JsonSerializer.Serialize(new
        {
            x = box.X, y = box.Y, width = box.Width, height = box.Height, unit = "px",
            quad = quad.Select(point => new[] { (int)Math.Round(point.X), (int)Math.Round(point.Y) }).ToArray(),
        });
    }

    /// <summary>The four corners of a print area marked by them, or <see langword="null"/> for a rectangle.</summary>
    public static IReadOnlyList<MockupPoint>? ParsePrintQuad(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            if (JsonNode.Parse(json)?["quad"] is not JsonArray corners || corners.Count != 4)
                return null;
            var points = new List<MockupPoint>();
            foreach (var corner in corners)
            {
                if (corner is not JsonArray { Count: 2 } pair || (double?)pair[0] is not double x || (double?)pair[1] is not double y)
                    return null;
                points.Add(new MockupPoint(x, y));
            }
            return points;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The pixel box around a print area's corners.</summary>
    public static MockupPosition QuadBounds(IReadOnlyList<MockupPoint> quad)
    {
        var left = (int)Math.Floor(quad.Min(point => point.X));
        var top = (int)Math.Floor(quad.Min(point => point.Y));
        var right = (int)Math.Ceiling(quad.Max(point => point.X));
        var bottom = (int)Math.Ceiling(quad.Max(point => point.Y));
        return new MockupPosition(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>
    /// Whether four corners make a print area: each inside the photo, going round it clockwise without crossing
    /// (top-left, top-right, bottom-right, bottom-left), and not flat.
    /// </summary>
    public static bool IsValidQuad(IReadOnlyList<MockupPoint> quad, int width, int height)
    {
        if (quad.Count != 4 || quad.Any(point => point.X < 0 || point.Y < 0 || point.X > width || point.Y > height))
            return false;
        // Every turn round a convex, clockwise (in image coordinates) quadrilateral bends the same way.
        for (var i = 0; i < 4; i++)
        {
            var (a, b, c) = (quad[i], quad[(i + 1) % 4], quad[(i + 2) % 4]);
            if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) <= 0)
                return false;
        }
        return true;
    }

    /// <summary>The design's rectangle fitted inside <paramref name="area"/> and centered, as the compositor places it.</summary>
    public static MockupPosition FitDesign(MockupPosition area, int designWidth, int designHeight)
    {
        var scale = Math.Min((double)area.Width / designWidth, (double)area.Height / designHeight);
        var width = Math.Clamp((int)Math.Round(designWidth * scale), 1, area.Width);
        var height = Math.Clamp((int)Math.Round(designHeight * scale), 1, area.Height);
        return new MockupPosition(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    /// <summary>
    /// Where a design's four corners go on a model photo whose print area is marked by its corners: the design as
    /// it sits in the mock-up template's print area, taken as shares of it, carried onto the corners by the
    /// perspective that turns a square into them. A straight print area gives a straight design.
    /// </summary>
    /// <param name="used">The print area the mock-up was made with.</param>
    /// <param name="templateArea">The mock-up template's own print area.</param>
    /// <param name="designWidth">The design image's width, to fit it as the mock-up did.</param>
    /// <param name="designHeight">The design image's height.</param>
    /// <param name="quad">The model photo's print area corners: top-left, top-right, bottom-right, bottom-left.</param>
    public static IReadOnlyList<MockupPoint> MapPlacementOnQuad(MockupPosition used, MockupPosition templateArea, int designWidth, int designHeight,
        IReadOnlyList<MockupPoint> quad)
    {
        var design = FitDesign(used, designWidth, designHeight);
        var u0 = (double)(design.X - templateArea.X) / templateArea.Width;
        var v0 = (double)(design.Y - templateArea.Y) / templateArea.Height;
        var u1 = (double)(design.X + design.Width - templateArea.X) / templateArea.Width;
        var v1 = (double)(design.Y + design.Height - templateArea.Y) / templateArea.Height;
        var toQuad = SquareToQuad(quad);
        return [toQuad(u0, v0), toQuad(u1, v0), toQuad(u1, v1), toQuad(u0, v1)];
    }

    /// <summary>
    /// The perspective transform that takes the unit square's corners (0,0), (1,0), (1,1), (0,1) to the
    /// quadrilateral's (Heckbert, "Fundamentals of Texture Mapping and Image Warping", 1989).
    /// </summary>
    internal static Func<double, double, MockupPoint> SquareToQuad(IReadOnlyList<MockupPoint> quad)
    {
        var (x0, y0, x1, y1, x2, y2, x3, y3) = (quad[0].X, quad[0].Y, quad[1].X, quad[1].Y, quad[2].X, quad[2].Y, quad[3].X, quad[3].Y);
        var (dx1, dx2, dx3) = (x1 - x2, x3 - x2, x0 - x1 + x2 - x3);
        var (dy1, dy2, dy3) = (y1 - y2, y3 - y2, y0 - y1 + y2 - y3);
        var denominator = dx1 * dy2 - dx2 * dy1;
        var g = Math.Abs(denominator) < 1e-9 ? 0 : (dx3 * dy2 - dx2 * dy3) / denominator;
        var h = Math.Abs(denominator) < 1e-9 ? 0 : (dx1 * dy3 - dx3 * dy1) / denominator;
        var (a, b, c) = (x1 - x0 + g * x1, x3 - x0 + h * x3, x0);
        var (d, e, f) = (y1 - y0 + g * y1, y3 - y0 + h * y3, y0);
        return (u, v) =>
        {
            var w = g * u + h * v + 1;
            return new MockupPoint((a * u + b * v + c) / w, (d * u + e * v + f) / w);
        };
    }

    /// <summary>Whether a print area is marked as followed automatically (see <see cref="SerializePrintArea(MockupPosition, bool)"/>).</summary>
    public static bool IsAutomaticPlacement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            return JsonNode.Parse(json)?["auto"] is JsonValue value && value.TryGetValue<bool>(out var automatic) && automatic;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions OverrideJson = new(JsonSerializerDefaults.Web);

    /// <summary>The mock-ups a job's config makes its own way, by slot key. Entries that can't be read are skipped.</summary>
    public static IReadOnlyDictionary<string, MockupOverrideDto> ReadOverrides(string config)
    {
        var result = new Dictionary<string, MockupOverrideDto>();
        try
        {
            if (JsonNode.Parse(config)?[OverridesConfigKey] is not JsonObject bySlot)
                return result;

            foreach (var (slotKey, value) in bySlot)
            {
                if (!TryReadSlotKey(slotKey, out _, out _) || value is not JsonObject)
                    continue;
                // An entry this version can't read is skipped, so one bad entry never stops the job's mock-ups.
                try
                {
                    if (value.Deserialize<MockupOverrideDto>(OverrideJson) is { MockupTemplateId: var templateId } item && templateId != Guid.Empty)
                        result[slotKey] = item;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

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
