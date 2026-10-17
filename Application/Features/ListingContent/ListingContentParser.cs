using System.Text.Json;
using APCS.Application.Features.ListingContent.Common;

namespace APCS.Application.Features.ListingContent;

public static class ListingContentParser
{
    public static GeneratedListingContent Parse(string rawResponse)
    {
        var json = rawResponse.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
                json = json[(firstLineEnd + 1)..lastFence].Trim();
        }

        var result = JsonSerializer.Deserialize<Response>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Gemini returned an empty listing response.");

        var title = result.Title?.Trim();
        var description = result.Description?.Trim();
        var tags = result.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Value))
            .Select(tag => new GeneratedListingTag(tag.Value!.Trim(), tag.Type?.Trim()))
            .GroupBy(tag => tag.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(13)
            .ToArray() ?? [];

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || tags.Length != 13)
            throw new InvalidOperationException("Gemini returned incomplete listing content.");

        return new GeneratedListingContent(title, description, tags);
    }

    private sealed record Response(string? Title, string? Description, Tag[]? Tags);
    private sealed record Tag(string? Value, string? Type);
}
