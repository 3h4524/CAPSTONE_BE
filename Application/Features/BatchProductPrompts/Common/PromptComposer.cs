using System.Text.RegularExpressions;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>Pure prompt synthesis shared by the service and mirrored by the frontend preview.</summary>
public static partial class PromptComposer
{
    /// <summary>The longest product description that is appended to a prompt.</summary>
    public const int MaximumDescriptionLength = 300;

    /// <summary>
    /// Builds the effective prompt: resolves {subject} {niche} {style} {keywords} in the base prompt
    /// (single or double curly braces), appends components missing from the base, then the product
    /// description, style modifiers, and negative terms as their own clause.
    /// </summary>
    public static string Compose(
        string basePrompt,
        string subject,
        string artStyle,
        string moodTone,
        string negativeTerms,
        string instructions,
        string niche,
        string styleModifiers,
        string keywords = "",
        string description = "")
    {
        // "{{subject}}" is accepted as an alias for "{subject}" so both spellings resolve cleanly.
        var source = DoubleBrace().Replace(basePrompt ?? string.Empty, "{$1}");
        var usedSubject = ContainsPlaceholder(source, "subject");
        var usedStyle = ContainsPlaceholder(source, "style");
        var usedKeywords = ContainsPlaceholder(source, "keywords");

        var resolved = source
            .Replace("{subject}", subject, StringComparison.OrdinalIgnoreCase)
            .Replace("{niche}", niche, StringComparison.OrdinalIgnoreCase)
            .Replace("{style}", artStyle, StringComparison.OrdinalIgnoreCase)
            .Replace("{keywords}", keywords.Trim(), StringComparison.OrdinalIgnoreCase);

        var parts = new List<string> { resolved.Trim() };
        if (!usedSubject && subject.Trim().Length > 0)
            parts.Add(subject.Trim());
        if (!usedStyle && artStyle.Trim().Length > 0)
            parts.Add(artStyle.Trim());
        if (!usedKeywords && keywords.Trim().Length > 0)
            parts.Add($"keywords: {keywords.Trim()}");
        parts.Add(moodTone.Trim());
        parts.Add(instructions.Trim());

        var combined = string.Empty;
        foreach (var part in parts.Where(part => part.Length > 0))
            combined = Append(combined, part);

        var trimmedDescription = description.Trim();
        if (trimmedDescription.Length > MaximumDescriptionLength)
            trimmedDescription = trimmedDescription[..MaximumDescriptionLength].TrimEnd();
        if (trimmedDescription.Length > 0)
            combined = Append(combined, $"Additional guidance: {trimmedDescription}");

        if (styleModifiers.Trim().Length > 0)
            combined = Append(combined, styleModifiers.Trim());

        if (negativeTerms.Trim().Length > 0)
            combined += $"\nNegative prompt: {negativeTerms.Trim()}";

        return combined;
    }

    // Joins with ", " but never leaves ".," or ",," when the previous text already ends in punctuation.
    private static string Append(string combined, string next)
    {
        if (combined.Length == 0) return next;
        return $"{combined.TrimEnd().TrimEnd('.', ',', ';')}, {next}";
    }

    private static bool ContainsPlaceholder(string source, string name) =>
        source.Contains($"{{{name}}}", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    public static string? NullIfEmpty(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleBrace();
}
