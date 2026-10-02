using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Storage;

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
}
