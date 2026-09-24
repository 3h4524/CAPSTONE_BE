namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>The structured pieces <see cref="PromptComposer"/> combines into one effective prompt.</summary>
public sealed record PromptComponents(
    string Subject,
    string ArtStyle,
    string MoodTone,
    string NegativeTerms,
    string Instructions,
    string BasePrompt = "",
    string Niche = "",
    string StyleModifiers = "");
