using System.Text.Json;

namespace APCS.Application.Features.StyleArtPresets.Common;

/// <summary>Shared limits and parsing for seller-managed art styles.</summary>
public static class StyleArtPresetRules
{
    public const int MaximumNameLength = 120;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumModifiersLength = 500;
    public const int MaximumRecommendations = 8;
    public const int MaximumRecommendationLength = 60;
    public const long MaximumPreviewBytes = 5 * 1024 * 1024;

    public static bool TryParseRecommendations(string? source, out string[] recommendations)
    {
        recommendations = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(source);
            if (parsed is null)
            {
                return false;
            }

            recommendations = parsed
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToArray();
            return recommendations.Length <= MaximumRecommendations
                && recommendations.All(item => item.Length <= MaximumRecommendationLength);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string StorageKey(Guid presetId) => $"style-art-presets/{presetId:N}";
}
