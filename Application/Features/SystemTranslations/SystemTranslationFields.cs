using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.SystemTranslations;

/// <summary>
/// The English text of each system catalog row that sellers read, keyed by the field names stored in its
/// <c>translations</c> document. Prompts, codes and filter values are left out on purpose.
/// </summary>
public static class SystemTranslationFields
{
    public const string Name = "name";
    public const string Description = "description";
    public const string StyleDescription = "styleDescription";
    public const string AiName = "aiName";
    public const string Continent = "continent";
    public const string Gender = "gender";
    public const string BodyType = "bodyType";
    public const string SkinTone = "skinTone";
    public const string Genre = "genre";
    public const string Mood = "mood";
    public const string Label = "label";
    public const string Group = "group";

    public static string SubtemplateName(string code) => $"subtemplates.{code}.name";

    public static string SubtemplateDescription(string code) => $"subtemplates.{code}.description";

    public static IReadOnlyDictionary<string, string> For(DesignTemplate template) =>
        Build((Name, template.Name), (StyleDescription, template.StyleDescription));

    public static IReadOnlyDictionary<string, string> For(AiCharacterModel model) =>
        Build(
            (Description, model.Description),
            (Continent, model.Continent),
            (Gender, model.Gender),
            (BodyType, model.BodyType),
            (SkinTone, model.SkinTone));

    public static IReadOnlyDictionary<string, string> For(MusicTrack track) =>
        Build((Genre, track.Genre), (Mood, track.Mood));

    public static IReadOnlyDictionary<string, string> For(SubscriptionPlan plan) =>
        Build((Description, plan.Description));

    public static IReadOnlyDictionary<string, string> For(Feature feature) =>
        Build((Label, feature.Label), (Description, feature.Description), (Group, feature.GroupName));

    public static IReadOnlyDictionary<string, string> For(VideoTemplate template)
    {
        var fields = new List<(string, string?)> { (Name, template.Name) };
        if (string.IsNullOrWhiteSpace(template.EffectsConfig))
            return Build([.. fields]);

        try
        {
            using var json = JsonDocument.Parse(template.EffectsConfig);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Build([.. fields]);

            var aiVideo = Child(root, "aiVideo");
            fields.Add((Description,
                ReadString(root, "description")
                ?? ReadString(Child(root, "videoPack"), "description")
                ?? ReadString(aiVideo, "description")));
            fields.Add((AiName, ReadString(aiVideo, "name")));

            if (aiVideo is { } ai && ai.TryGetProperty("subtemplates", out var subtemplates) && subtemplates.ValueKind == JsonValueKind.Array)
            {
                foreach (var subtemplate in subtemplates.EnumerateArray())
                {
                    if (ReadString(subtemplate, "code") is not { } code)
                        continue;
                    fields.Add((SubtemplateName(code), ReadString(subtemplate, "name")));
                    fields.Add((SubtemplateDescription(code), ReadString(subtemplate, "description")));
                }
            }
        }
        catch (JsonException)
        {
        }

        return Build([.. fields]);
    }

    private static IReadOnlyDictionary<string, string> Build(params (string Key, string? Value)[] fields)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result[key] = value;
        }

        return result;
    }

    private static JsonElement? Child(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var child)
        && child.ValueKind == JsonValueKind.Object
            ? child
            : null;

    private static string? ReadString(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out var child)
        && child.ValueKind == JsonValueKind.String
            ? child.GetString()
            : null;
}
