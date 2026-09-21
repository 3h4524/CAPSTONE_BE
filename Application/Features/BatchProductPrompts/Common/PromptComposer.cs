namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>Pure prompt synthesis shared by the service and mirrored by the frontend preview.</summary>
public static class PromptComposer
{
    /// <summary>
    /// Builds the effective prompt: resolves {subject} {niche} {style} in the base prompt,
    /// appends components missing from the base, then style modifiers, then negative terms
    /// as their own clause.
    /// </summary>
    public static string Compose(
        string basePrompt,
        string subject,
        string artStyle,
        string moodTone,
        string negativeTerms,
        string instructions,
        string niche,
        string styleModifiers)
    {
        var source = basePrompt ?? string.Empty;
        var usedSubject = ContainsPlaceholder(source, "subject");
        var usedStyle = ContainsPlaceholder(source, "style");

        var resolved = source
            .Replace("{subject}", subject, StringComparison.OrdinalIgnoreCase)
            .Replace("{niche}", niche, StringComparison.OrdinalIgnoreCase)
            .Replace("{style}", artStyle, StringComparison.OrdinalIgnoreCase);

        var parts = new List<string> { resolved.Trim() };
        if (!usedSubject && subject.Trim().Length > 0)
            parts.Add(subject.Trim());
        if (!usedStyle && artStyle.Trim().Length > 0)
            parts.Add(artStyle.Trim());
        parts.Add(moodTone.Trim());
        parts.Add(instructions.Trim());

        var combined = string.Join(", ", parts.Where(part => part.Length > 0));

        if (styleModifiers.Trim().Length > 0)
        {
            combined = combined.Length > 0 ? $"{combined}, {styleModifiers.Trim()}" : styleModifiers.Trim();
        }

        if (negativeTerms.Trim().Length > 0)
        {
            combined += $"\nNegative prompt: {negativeTerms.Trim()}";
        }

        return combined;
    }

    private static bool ContainsPlaceholder(string source, string name) =>
        source.Contains($"{{{name}}}", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static string? NullIfEmpty(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
