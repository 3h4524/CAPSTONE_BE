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

    // ---- Print areas: a template's main area and up to three more, such as a sleeve or the back of the neck ----

    /// <summary>The key of a template's main print area: the one kept at the top of its JSON, which code reading a single area uses.</summary>
    public const string MainAreaKey = "main";
    public const int MaximumPrintAreas = 4;
    public const int MaximumAreaNameLength = 40;
    public const string AreaKeyPattern = "^[a-z0-9][a-z0-9_-]{0,23}$";
    public const double MaximumRotation = 180;

    /// <summary>What a job prints in each print area of a template, by template id and then area key.</summary>
    public const string AreaSourcesConfigKey = "mockupAreaSources";

    /// <summary>The design the mock-up is made of.</summary>
    public const string SourceSame = "same";
    /// <summary>Another design, named by id.</summary>
    public const string SourceDesign = "design";
    /// <summary>Nothing.</summary>
    public const string SourceNone = "none";

    public const string ViewFront = "front";
    public const string ViewBack = "back";
    public const string ViewLeft = "left";
    public const string ViewRight = "right";
    public const string ViewSide = "side";
    public static readonly IReadOnlyList<string> Views = [ViewFront, ViewBack, ViewLeft, ViewRight, ViewSide];

    /// <summary>The other sides a template can have photos of, besides its own front photo.</summary>
    public static readonly IReadOnlyList<string> OtherViews = [ViewBack, ViewLeft, ViewRight];

    // ---- Views: one template, a photo of each side ----
    // A template's photo is its front. Each other side (back, left sleeve, right sleeve) is a template of its own,
    // hidden from lists and kept with its main template: the main one's config names it under "views", and its own
    // config names the main one under "mainTemplateId". Each side has its own photo, helper images and print areas.

    private const string ViewLinksKey = "views";
    private const string MainTemplateKey = "mainTemplateId";

    /// <summary>The templates of a template's other sides, by side.</summary>
    public static IReadOnlyDictionary<string, Guid> ViewTemplateIds(string? json)
    {
        var result = new Dictionary<string, Guid>();
        try
        {
            if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json)?[ViewLinksKey] is not JsonObject links)
                return result;
            foreach (var (view, value) in links)
            {
                if (OtherViews.Contains(view) && value is JsonValue text && text.TryGetValue<string>(out var id) && Guid.TryParse(id, out var templateId))
                    result[view] = templateId;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>The template this one is another side of, or <see langword="null"/> for a template of its own.</summary>
    public static Guid? MainTemplateId(string? json)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(json) && JsonNode.Parse(json) is JsonObject node
                && ReadString(node, MainTemplateKey) is { } id && Guid.TryParse(id, out var mainId) ? mainId : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Whether a template is only another side of a template, shown with it rather than on its own.</summary>
    public static bool IsOtherView(MockupTemplate template) => MainTemplateId(template.PrintAreaConfig) is not null;

    /// <summary>The config with its links to other sides set: the main template's sides, or the template it is a side of.</summary>
    public static string WithViewLinks(string json, IReadOnlyDictionary<string, Guid>? views, Guid? mainTemplateId)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        root.Remove(ViewLinksKey);
        root.Remove(MainTemplateKey);
        if (views is { Count: > 0 })
        {
            var links = new JsonObject();
            foreach (var view in OtherViews.Where(views.ContainsKey))
                links[view] = views[view].ToString();
            root[ViewLinksKey] = links;
        }
        if (mainTemplateId is Guid mainId)
            root[MainTemplateKey] = mainId.ToString();
        return root.ToJsonString();
    }

    /// <summary>The config with the side its photo shows set.</summary>
    public static string WithView(string json, string view)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        if (view == ViewFront)
            root.Remove("view");
        else
            root["view"] = view;
        return root.ToJsonString();
    }

    /// <summary>A new config keeps the links to other sides the old one had.</summary>
    public static string KeepViewLinks(string oldJson, string newJson)
    {
        var views = ViewTemplateIds(oldJson);
        var mainId = MainTemplateId(oldJson);
        return views.Count == 0 && mainId is null ? newJson : WithViewLinks(newJson, views, mainId);
    }

    /// <summary>Which side of the garment a template photo shows, and its print areas, the main one first.</summary>
    public sealed record MockupPrintLayout(string View, IReadOnlyList<MockupAreaDto> Areas)
    {
        public MockupAreaDto Main => Areas[0];
    }

    /// <summary>A print area as it is used for one mock-up: where it goes and what is printed in it.</summary>
    public sealed record ResolvedArea(MockupAreaDto Area, string Source, Guid? DesignImageId)
    {
        public MockupPosition Position => AreaPosition(Area);
    }

    public static MockupPosition AreaPosition(MockupAreaDto area) => new(area.X, area.Y, area.Width, area.Height);

    /// <summary>
    /// Every print area of a template's <c>print_area_config</c>, the main one first. A config of one rectangle, as
    /// every template had before, is one main area on the front. Null when not even the main area can be read.
    /// </summary>
    public static MockupPrintLayout? ParsePrintLayout(string? json)
    {
        if (ParsePrintArea(json) is not { } main)
            return null;

        var mainArea = new MockupAreaDto(MainAreaKey, null, main.X, main.Y, main.Width, main.Height, Source: SourceSame);
        try
        {
            var node = JsonNode.Parse(json!) as JsonObject;
            var view = ReadString(node, "view") is { } given && Views.Contains(given) ? given : ViewFront;
            var areas = new List<MockupAreaDto>
            {
                mainArea with { Name = ReadName(node), Rotation = ReadRotation(node), Source = ReadTemplateSource(node, SourceSame) },
            };
            if (node?["areas"] is JsonArray more)
            {
                foreach (var item in more)
                {
                    if (areas.Count >= MaximumPrintAreas)
                        break;
                    if (item is not JsonObject area || ReadString(area, "key") is not { } key
                        || !System.Text.RegularExpressions.Regex.IsMatch(key, AreaKeyPattern) || areas.Any(known => known.Key == key))
                        continue;
                    if (ReadInt(area, "x") is not int x || ReadInt(area, "y") is not int y
                        || ReadInt(area, "width") is not (> 0 and var width) || ReadInt(area, "height") is not (> 0 and var height))
                        continue;
                    areas.Add(new MockupAreaDto(key, ReadName(area), x, y, width, height, ReadRotation(area), ReadTemplateSource(area, SourceNone)));
                }
            }
            return new MockupPrintLayout(view, areas);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return new MockupPrintLayout(ViewFront, [mainArea]);
        }
    }

    /// <summary>
    /// The <c>print_area_config</c> of a template with these print areas. The main one stays at the top as a plain
    /// rectangle, so a template of one straight area on the front is stored exactly as before.
    /// </summary>
    public static string SerializePrintLayout(string? view, IReadOnlyList<MockupAreaDto> areas)
    {
        var main = areas.First(area => area.Key == MainAreaKey);
        var root = new JsonObject
        {
            ["x"] = main.X,
            ["y"] = main.Y,
            ["width"] = main.Width,
            ["height"] = main.Height,
            ["unit"] = "px",
        };
        if (!string.IsNullOrWhiteSpace(main.Name))
            root["name"] = main.Name.Trim();
        if (NormalizeRotation(main.Rotation) is var rotation and not 0)
            root["rotation"] = rotation;
        if (main.Source == SourceNone)
            root["source"] = SourceNone;
        if (view is not null && view != ViewFront && Views.Contains(view))
            root["view"] = view;

        var more = areas.Where(area => area.Key != MainAreaKey).ToList();
        if (more.Count > 0)
        {
            root["areas"] = new JsonArray(more.Select(area =>
            {
                var item = new JsonObject { ["key"] = area.Key };
                if (!string.IsNullOrWhiteSpace(area.Name))
                    item["name"] = area.Name.Trim();
                item["x"] = area.X;
                item["y"] = area.Y;
                item["width"] = area.Width;
                item["height"] = area.Height;
                if (NormalizeRotation(area.Rotation) is var turned and not 0)
                    item["rotation"] = turned;
                item["source"] = area.Source == SourceSame ? SourceSame : SourceNone;
                return (JsonNode?)item;
            }).ToArray());
        }
        return root.ToJsonString();
    }

    /// <summary>Which side of the garment a template photo shows: front unless it is marked otherwise.</summary>
    public static string ParseView(string? json) => ParsePrintLayout(json)?.View ?? ViewFront;

    /// <summary>Degrees rounded to a tenth and kept within -180 to 180.</summary>
    public static double NormalizeRotation(double degrees)
    {
        if (!double.IsFinite(degrees))
            return 0;
        var turned = Math.Round(degrees % 360, 1);
        if (turned > MaximumRotation) turned -= 360;
        if (turned < -MaximumRotation) turned += 360;
        return turned == 0 ? 0 : turned;
    }

    /// <summary>Whether a print area, turned as it is, lies wholly on a photo of this size.</summary>
    public static bool AreaFits(MockupAreaDto area, int photoWidth, int photoHeight)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return false;
        var box = MockupGeometry.RotatedBounds(AreaPosition(area), NormalizeRotation(area.Rotation));
        return box.X >= 0 && box.Y >= 0 && box.X + box.Width <= photoWidth && box.Y + box.Height <= photoHeight;
    }

    /// <summary>
    /// Why these print areas can't be kept, or <see langword="null"/> when they can. A template's are all its areas
    /// with the main one among them, and print the mock-up's design or nothing by default; a mock-up's own may name
    /// some of its template's areas and print another design.
    /// </summary>
    public static string? AreasProblem(IReadOnlyList<MockupAreaDto>? areas, bool forTemplate)
    {
        if (areas is null || areas.Count == 0)
            return "Add at least one print area.";
        if (areas.Count > MaximumPrintAreas)
            return $"A template has at most {MaximumPrintAreas} print areas.";
        if (areas.Any(area => area?.Key is null || !System.Text.RegularExpressions.Regex.IsMatch(area.Key, AreaKeyPattern)))
            return "Each print area needs a short key of lowercase letters, digits, '-' or '_'.";
        if (areas.Select(area => area.Key).Distinct().Count() != areas.Count)
            return "Print areas must not repeat.";
        if (forTemplate && areas.All(area => area.Key != MainAreaKey))
            return "A template needs its main print area.";
        foreach (var area in areas)
        {
            if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0)
                return "A print area needs a position of 0 or more and a size above 0.";
            if (!double.IsFinite(area.Rotation) || Math.Abs(area.Rotation) > MaximumRotation)
                return $"A print area can be turned from -{MaximumRotation} to {MaximumRotation} degrees.";
            if (area.Name is { Length: > MaximumAreaNameLength })
                return $"A print area's name has at most {MaximumAreaNameLength} characters.";
            switch (area.Source)
            {
                case null or SourceSame or SourceNone when area.DesignImageId is null:
                    break;
                case SourceDesign when !forTemplate && area.DesignImageId is Guid id && id != Guid.Empty:
                    break;
                case SourceDesign when !forTemplate:
                    return "Choose the design to print in the area.";
                default:
                    return forTemplate
                        ? "A template's print area prints the mock-up's design or nothing."
                        : "A print area prints the mock-up's design, another design or nothing.";
            }
        }
        return null;
    }

    /// <summary>Why what a job prints in a print area can't be kept, or <see langword="null"/> when it can.</summary>
    public static string? AreaSourceProblem(string areaKey, MockupAreaSourceDto? source) =>
        !System.Text.RegularExpressions.Regex.IsMatch(areaKey ?? "", AreaKeyPattern) ? "Each print area needs a short key of lowercase letters, digits, '-' or '_'."
        : source is null ? "Choose what is printed in each print area."
        : source.Kind is SourceSame or SourceNone && source.DesignImageId is null ? null
        : source.Kind == SourceDesign && source.DesignImageId is Guid id && id != Guid.Empty ? null
        : source.Kind == SourceDesign ? "Choose the design to print in the area."
        : "A print area prints the mock-up's design, another design or nothing.";

    /// <summary>
    /// The template's print areas as used for one mock-up: each where the mock-up's own settings put it, else where
    /// the template has it, printing what the mock-up's own settings say, else what the job chose for it, else the
    /// template's default.
    /// </summary>
    public static IReadOnlyList<ResolvedArea> ResolveAreas(MockupPrintLayout layout, MockupOverrideDto? custom,
        IReadOnlyDictionary<string, MockupAreaSourceDto>? jobSources)
    {
        var result = new List<ResolvedArea>();
        foreach (var area in layout.Areas)
        {
            var own = custom?.Areas?.FirstOrDefault(item => item.Key == area.Key);
            var placed = own is not null
                ? area with { X = own.X, Y = own.Y, Width = own.Width, Height = own.Height, Rotation = NormalizeRotation(own.Rotation) }
                : area.Key == MainAreaKey && custom?.PrintArea is { } moved
                    ? area with { X = moved.X, Y = moved.Y, Width = moved.Width, Height = moved.Height }
                    : area with { Rotation = NormalizeRotation(area.Rotation) };

            var (source, designId) = own?.Source is { } chosen ? (chosen, own.DesignImageId)
                : jobSources is not null && jobSources.TryGetValue(area.Key, out var job) ? (job.Kind, job.DesignImageId)
                : (area.Source ?? (area.Key == MainAreaKey ? SourceSame : SourceNone), (Guid?)null);
            result.Add(new ResolvedArea(placed, source, source == SourceDesign ? designId : null));
        }
        return result;
    }

    /// <summary>What a job prints in the print areas of its templates, by template id and then area key. Entries that can't be read are skipped.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>> ReadAreaSources(string config)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>>();
        try
        {
            if (JsonNode.Parse(config)?[AreaSourcesConfigKey] is not JsonObject byTemplate)
                return result;
            foreach (var (templateKey, value) in byTemplate)
            {
                if (!Guid.TryParse(templateKey, out var templateId) || value is not JsonObject byArea)
                    continue;
                var sources = new Dictionary<string, MockupAreaSourceDto>();
                foreach (var (areaKey, item) in byArea)
                {
                    if (item is not JsonObject entry || ReadString(entry, "kind") is not { } kind)
                        continue;
                    var designId = Guid.TryParse(ReadString(entry, "designImageId"), out var id) ? id : (Guid?)null;
                    var source = new MockupAreaSourceDto(kind, designId);
                    if (AreaSourceProblem(areaKey, source) is null)
                        sources[areaKey] = source;
                }
                if (sources.Count > 0)
                    result[templateId] = sources;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>The job's config with what it prints in its templates' print areas; none removes the entry.</summary>
    public static void WriteAreaSources(JsonObject root, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>> sources)
    {
        if (sources.Count == 0)
        {
            root.Remove(AreaSourcesConfigKey);
            return;
        }
        var byTemplate = new JsonObject();
        foreach (var (templateId, byArea) in sources)
        {
            var areas = new JsonObject();
            foreach (var (areaKey, source) in byArea)
            {
                var entry = new JsonObject { ["kind"] = source.Kind };
                if (source.DesignImageId is Guid id)
                    entry["designImageId"] = id.ToString();
                areas[areaKey] = entry;
            }
            byTemplate[templateId.ToString()] = areas;
        }
        root[AreaSourcesConfigKey] = byTemplate;
    }

    private static string? ReadString(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? ReadInt(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? (int)Math.Round(number) : null;

    private static string? ReadName(JsonObject? node) =>
        ReadString(node, "name") is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim()[..Math.Min(name.Trim().Length, MaximumAreaNameLength)] : null;

    private static double ReadRotation(JsonObject? node) =>
        node?["rotation"] is JsonValue value && value.TryGetValue<double>(out var degrees) ? NormalizeRotation(degrees) : 0;

    private static string ReadTemplateSource(JsonObject? node, string fallback) =>
        ReadString(node, "source") is (SourceSame or SourceNone) and var source ? source : fallback;

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
