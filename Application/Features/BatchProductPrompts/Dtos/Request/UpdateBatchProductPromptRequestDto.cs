namespace APCS.Application.Features.BatchProductPrompts.Dtos.Request;

/// <summary>Overrides the prompt components of one batch row.</summary>
/// <param name="Subject">Custom subject.</param>
/// <param name="ArtStyle">Custom art style.</param>
/// <param name="MoodTone">Custom mood and tone.</param>
/// <param name="NegativeTerms">Custom negative terms, empty for none.</param>
/// <param name="Instructions">Custom product instructions, empty for none.</param>
/// <param name="CustomPrompt">
/// The product's own image prompt, which replaces the design template: empty removes it, null leaves it as it is.
/// </param>
public sealed record UpdateBatchProductPromptRequestDto(
    string Subject,
    string ArtStyle,
    string MoodTone,
    string NegativeTerms,
    string Instructions,
    string? CustomPrompt = null);
