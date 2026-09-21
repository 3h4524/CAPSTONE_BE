using System.Text.RegularExpressions;

namespace APCS.Application.Features.DesignTemplates.Common;

/// <summary>Provides placeholder inspection for design-template prompts.</summary>
public static partial class DesignTemplatePromptRules
{
    public static bool ContainsOnlySupportedPlaceholders(string prompt) =>
        PlaceholderPattern().Matches(prompt).All(match =>
            DesignTemplatePlaceholders.All.Contains(match.Groups[1].Value, StringComparer.Ordinal));

    public static bool ContainsSubjectPlaceholder(string prompt) =>
        prompt.Contains("{subject}", StringComparison.Ordinal);

    public static bool ContainsUnresolvedPlaceholder(string prompt) => PlaceholderPattern().IsMatch(prompt);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
