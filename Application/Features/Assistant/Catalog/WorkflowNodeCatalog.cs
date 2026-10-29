using System.Text.Json;
using System.Text.Json.Nodes;

namespace APCS.Application.Features.Assistant.Catalog;

public sealed record NodeFieldOption(string Value, string Label);

public sealed record NodeFieldSpec(
    string Name,
    string Kind,
    string Label,
    IReadOnlyList<NodeFieldOption>? Options = null,
    int? Min = null,
    int? Max = null,
    int? MaxLength = null,
    string? Source = null,
    bool Optional = false,
    string? Note = null);

public sealed record NodeSpec(
    string Type,
    string Label,
    string Description,
    int? RunOrder,
    IReadOnlyList<NodeFieldSpec> Fields,
    string? Note = null);

public static class WorkflowNodeCatalog
{
    public static readonly IReadOnlyList<string> RunOrder =
    [
        "product-input", "prompt-synthesis", "design-image", "design-approval", "apply-mockup",
        "approval-gate", "generate-video", "review-video", "export-zip"
    ];

    public static readonly IReadOnlyList<string> ConnectionRules =
    [
        $"Steps connect in one direction, in this run order: {string.Join(" -> ", RunOrder)}. Only the steps that are on the canvas count.",
        "A step connects only to the NEXT step that is on the canvas. It cannot skip a step that is present (for example it cannot jump over Mockup Approval).",
        "generate-listing is outside the run order: it may follow or precede any step, and needs exactly one incoming connection.",
        "A node cannot connect to itself, a connection cannot repeat, and a connection cannot create a loop.",
        "To insert a step between A and B: disconnect A->B first, then connect A->new and new->B."
    ];

    private static readonly IReadOnlyList<NodeFieldOption> AspectRatios =
        new[] { "1:1", "16:9", "9:16", "4:3", "3:4" }.Select(ratio => new NodeFieldOption(ratio, ratio)).ToArray();

    private static readonly IReadOnlyList<NodeFieldOption> ApprovalModes =
    [
        new("manual", "Manual review"),
        new("auto", "Approve automatically")
    ];

    public static readonly IReadOnlyList<NodeSpec> Nodes =
    [
        new("product-input", "Product input", "Starts the workflow with the products of a batch.", Order("product-input"),
        [
            new("batchId", "source", "Batch", Source: "batches", Note: "Use list_batches for ids."),
            new("productId", "source", "Product for the video", Source: "batch-products", Note: "Use list_batch_products for ids.")
        ]),
        new("prompt-synthesis", "Prompt synthesis", "Combines a design template and art style into a design prompt.", Order("prompt-synthesis"),
        [
            new("designTemplateId", "source", "Design template", Source: "design-templates", Note: "Use list_design_templates for ids."),
            new("stylePresetId", "source", "Art style", Source: "style-presets", Optional: true, Note: "Use list_style_presets for ids; empty string means no style."),
            new("instructions", "textarea", "Extra instructions", MaxLength: 500)
        ]),
        new("design-image", "Design image", "Generates design variations for every product.", Order("design-image"),
        [
            new("imageProvider", "select", "Image model",
                [new("gemini", "Gemini (Nano Banana 2), about $0.067 per image"), new("ideogram", "Ideogram 4.0, about $0.03 per 2K image, needs an Ideogram key")]),
            new("variants", "number", "Variants per product", Min: 1, Max: 4),
            new("aspectRatio", "select", "Aspect ratio", AspectRatios)
        ]),
        new("design-approval", "Design approval", "Only approved designs get mockups.", Order("design-approval"),
        [
            new("mode", "select", "Approval mode", ApprovalModes)
        ]),
        new("apply-mockup", "Apply mock-up", "Places approved designs onto mockup templates.", Order("apply-mockup"),
        [
            new("mockupTemplateIds", "id-list", "Mockup templates", Source: "mockup-templates",
                Note: "Use list_mockup_templates for ids. Colors per template are set in the UI.")
        ]),
        new("approval-gate", "Mockup Approval", "Reviews each mockup before rendering.", Order("approval-gate"),
        [
            new("mode", "select", "Approval mode", [new("manual", "Manual review")])
        ]),
        new("generate-video", "Generate Video", "Builds a promo video from the approved mockups.", Order("generate-video"),
        [
            new("outputFormat", "select", "Output format", [new("square", "Square 1080x1080"), new("portrait", "Portrait 1080x1920"), new("landscape", "Landscape 1920x1080")]),
            new("textOverlay", "text", "Text overlay", MaxLength: 120),
            new("standardOptions.transition", "select", "Transition (Standard mode)",
                [new("none", "None"), new("fade", "Fade"), new("swipe", "Swipe"), new("zoom", "Zoom"), new("flash", "Flash"), new("whip_pan", "Whip pan"), new("blur", "Blur")],
                Note: "Send as nested config, for example {\"standardOptions\":{\"transition\":\"fade\"}}.")
        ], "Video mode, AI scenes and motion are set in the UI because AI generation costs money."),
        new("review-video", "Review Video", "Approve the exact video version before export.", Order("review-video"), []),
        new("generate-listing", "Listing content", "Writes the SEO title, 13 tags and description for each product.", null,
        [
            new("model", "select", "Language model", [new("gpt-4o", "GPT-4o"), new("gemini", "Gemini")]),
            new("tone", "select", "Tone", [new("friendly", "Friendly"), new("professional", "Professional"), new("playful", "Playful")]),
            new("includeSeoScore", "switch", "SEO score")
        ]),
        new("export-zip", "Export ZIP", "Packages the final assets into a ZIP file.", Order("export-zip"), [])
    ];

    public static NodeSpec? Find(string type) => Nodes.FirstOrDefault(node => node.Type == type);

    public static int OrderOf(string type) => RunOrder.ToList().IndexOf(type);

    public static string LabelOf(string type) => Find(type)?.Label ?? type;

    private static int? Order(string type) => OrderOf(type) switch { < 0 => null, var index => index };

    public static string? ValidateConfig(string nodeType, JsonObject patch)
    {
        var spec = Find(nodeType);
        if (spec is null || spec.Fields.Count == 0)
            return $"'{nodeType}' has no settings the assistant can change.";

        var errors = new List<string>();
        foreach (var (key, value) in Flatten(patch))
        {
            var field = spec.Fields.FirstOrDefault(item => item.Name == key);
            if (field is null)
            {
                errors.Add($"'{key}' is not a setting of {spec.Label}. Valid settings: {string.Join(", ", spec.Fields.Select(item => item.Name))}.");
                continue;
            }

            var problem = ValidateValue(field, value);
            if (problem is not null)
                errors.Add($"{field.Name}: {problem}");
        }

        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    private static string? ValidateValue(NodeFieldSpec field, JsonNode? value)
    {
        switch (field.Kind)
        {
            case "select":
                var choice = value is JsonValue stringValue && stringValue.TryGetValue<string>(out var text) ? text : null;
                return field.Options!.Any(option => option.Value == choice)
                    ? null
                    : $"must be one of {string.Join(", ", field.Options!.Select(option => option.Value))}.";
            case "number":
                if (value is not JsonValue numberValue || !numberValue.TryGetValue<double>(out var number) || number != Math.Floor(number))
                    return "must be a whole number.";
                return number >= field.Min && number <= field.Max ? null : $"must be between {field.Min} and {field.Max}.";
            case "switch":
                return value is JsonValue boolValue && boolValue.TryGetValue<bool>(out _) ? null : "must be true or false.";
            case "text":
            case "textarea":
                return value is JsonValue textValue && textValue.TryGetValue<string>(out var content) && content.Length <= field.MaxLength
                    ? null
                    : $"must be text of at most {field.MaxLength} characters.";
            case "source":
                return value is JsonValue idValue && idValue.TryGetValue<string>(out var id)
                    && (string.IsNullOrEmpty(id) && field.Optional || Guid.TryParse(id, out _))
                    ? null
                    : "must be an id (GUID) taken from the matching list tool.";
            case "id-list":
                return value is JsonArray array && array.All(item => item is JsonValue idItem && idItem.TryGetValue<string>(out var itemId) && Guid.TryParse(itemId, out _))
                    ? null
                    : "must be an array of ids (GUID) taken from the matching list tool.";
            default:
                return null;
        }
    }

    private static IEnumerable<(string Key, JsonNode? Value)> Flatten(JsonObject patch, string prefix = "")
    {
        foreach (var (key, value) in patch)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            if (value is JsonObject nested)
            {
                foreach (var child in Flatten(nested, path))
                    yield return child;
            }
            else
            {
                yield return (path, value);
            }
        }
    }

    public static JsonObject ExpandDottedKeys(JsonObject patch)
    {
        var result = new JsonObject();
        foreach (var (key, value) in patch)
        {
            var parts = key.Split('.');
            var target = result;
            foreach (var part in parts[..^1])
            {
                if (target[part] is not JsonObject child)
                {
                    child = new JsonObject();
                    target[part] = child;
                }

                target = child;
            }

            target[parts[^1]] = value is JsonObject nested ? ExpandDottedKeys(nested) : value?.DeepClone();
        }

        return result;
    }

    public static void DeepMerge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch)
        {
            if (value is JsonObject nested && target[key] is JsonObject existing)
                DeepMerge(existing, nested);
            else
                target[key] = value?.DeepClone();
        }
    }
}
