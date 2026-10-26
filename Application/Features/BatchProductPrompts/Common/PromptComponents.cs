namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>The structured pieces <see cref="PromptComposer"/> combines into one effective prompt.</summary>
/// <param name="OwnPrompt">
/// Made from the product's own image prompt, which sets the look itself: no art style or style preset applies to it,
/// whether it comes from the product, the job or the Seller's changes to this row.
/// </param>
public sealed record PromptComponents(
    string Subject,
    string ArtStyle,
    string MoodTone,
    string NegativeTerms,
    string Instructions,
    string BasePrompt = "",
    string Niche = "",
    string StyleModifiers = "",
    string Keywords = "",
    string Description = "",
    bool OwnPrompt = false);
