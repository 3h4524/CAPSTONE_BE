namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>Shared limits for per-row prompt overrides.</summary>
public static class PromptRules
{
    public const int MaximumSubjectLength = 200;
    public const int MaximumArtStyleLength = 100;
    public const int MaximumMoodToneLength = 200;
    public const int MaximumNegativeTermsLength = 500;
    public const int MaximumInstructionsLength = 1000;
    public const int MaximumEffectiveLength = 1000;

    /// <summary>The longest image prompt a Seller may write for a product, the length a prompt from a template may reach.</summary>
    public const int MaximumCustomPromptLength = 1000;
    public const string FallbackNiche = "general";
}
