using System.Globalization;
using System.Text.Json;

namespace APCS.Application.Features.StyleArtPresets.Common;

public static class StyleArtPresetTranslations
{
    public sealed record LocalizedText(string Name, string Description, string Recommendations);

    public sealed record ViTranslation(string Name, string Description, IReadOnlyList<string> Recommendations);

    public sealed record TranslationState(bool Manual, string Status, int Attempts, DateTime? NextRetryAt);

    public static LocalizedText Localize(
        string name,
        string description,
        string recommendations,
        string? translations,
        string culture)
    {
        if (culture != "vi" || string.IsNullOrWhiteSpace(translations))
            return new LocalizedText(name, description, recommendations);

        var vi = ParseViDocument(translations);
        if (vi is null)
            return new LocalizedText(name, description, recommendations);

        return new LocalizedText(
            vi.Name.Length > 0 ? vi.Name : name,
            vi.Description.Length > 0 ? vi.Description : description,
            vi.Recommendations.Count > 0 ? SerializeRecommendations(vi.Recommendations) : recommendations);
    }

    public static ViTranslation? ParseViDocument(string? translations)
    {
        if (string.IsNullOrWhiteSpace(translations))
            return null;

        try
        {
            using var document = JsonDocument.Parse(translations);
            if (!document.RootElement.TryGetProperty("vi", out var vi) || vi.ValueKind != JsonValueKind.Object)
                return null;

            var name = ReadString(vi, "name") ?? string.Empty;
            var description = ReadString(vi, "description") ?? string.Empty;
            var recommendations = ReadRecommendations(vi);
            return new ViTranslation(name, description, recommendations);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static TranslationState? ParseState(string? translations)
    {
        if (string.IsNullOrWhiteSpace(translations))
            return null;

        try
        {
            using var document = JsonDocument.Parse(translations);
            var root = document.RootElement;
            var manual = root.TryGetProperty("manual", out var manualValue) && manualValue.ValueKind == JsonValueKind.True;
            var status = root.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.String
                ? statusValue.GetString() ?? "ok"
                : "ok";
            var attempts = root.TryGetProperty("attempts", out var attemptsValue)
                && attemptsValue.ValueKind == JsonValueKind.Number
                && attemptsValue.TryGetInt32(out var count)
                    ? count
                    : 0;
            DateTime? nextRetryAt = null;
            if (root.TryGetProperty("nextRetryAt", out var nextValue)
                && nextValue.ValueKind == JsonValueKind.String
                && DateTime.TryParse(nextValue.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                nextRetryAt = parsed;
            }

            return new TranslationState(manual, status, attempts, nextRetryAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializeDocument(ViTranslation? vi, bool manual, string status, int attempts, DateTime? nextRetryAt)
    {
        var document = new Dictionary<string, object?>
        {
            ["manual"] = manual,
            ["status"] = status,
            ["attempts"] = attempts
        };
        if (nextRetryAt.HasValue)
            document["nextRetryAt"] = nextRetryAt.Value.ToString("o", CultureInfo.InvariantCulture);
        if (vi is not null)
            document["vi"] = new Dictionary<string, object?>
            {
                ["name"] = vi.Name,
                ["description"] = vi.Description,
                ["recommendations"] = vi.Recommendations
            };

        return JsonSerializer.Serialize(document);
    }

    public static string SerializeRecommendations(IReadOnlyList<string> recommendations) =>
        JsonSerializer.Serialize(recommendations);

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadRecommendations(JsonElement element)
    {
        if (!element.TryGetProperty("recommendations", out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }
}
