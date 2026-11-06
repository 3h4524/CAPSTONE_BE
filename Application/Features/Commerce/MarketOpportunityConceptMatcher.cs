namespace APCS.Application.Features.Commerce;

internal static class MarketOpportunityConceptMatcher
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "for", "from", "gift", "gifts", "handmade", "custom",
        "personalized", "personalised", "edition", "classic", "modern", "minimalist",
        "limited", "new", "set", "collection", "style", "design", "the", "with"
    };

    public static double Similarity(string? left, string? right)
    {
        var leftTokens = CoreTokens(left);
        var rightTokens = CoreTokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    public static string ConceptKey(string? title, string? niche)
    {
        var tokens = CoreTokens($"{niche} {title}")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(' ', tokens);
    }

    private static IReadOnlyCollection<string> CoreTokens(string? value) =>
        (value ?? string.Empty)
            .Split([' ', ',', '-', '_', '|', ':', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 3 && !Modifiers.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
