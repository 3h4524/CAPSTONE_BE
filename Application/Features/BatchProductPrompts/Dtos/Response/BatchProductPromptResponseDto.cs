namespace APCS.Application.Features.BatchProductPrompts.Dtos.Response;

/// <summary>The effective prompt of one batch row.</summary>
/// <param name="RowId">The batch row id.</param>
/// <param name="Subject">The effective subject.</param>
/// <param name="ArtStyle">The effective art style.</param>
/// <param name="MoodTone">The effective mood and tone.</param>
/// <param name="NegativeTerms">The effective negative terms.</param>
/// <param name="Instructions">The effective instructions.</param>
/// <param name="DefaultEffectivePrompt">The synthesized default prompt.</param>
/// <param name="DefaultBasePrompt">The template base prompt used for synthesis.</param>
/// <param name="DefaultNiche">The niche used for synthesis.</param>
/// <param name="DefaultStyleModifiers">The style modifiers used for synthesis.</param>
/// <param name="EffectivePrompt">The combined prompt string.</param>
/// <param name="CharacterCount">The combined prompt length.</param>
/// <param name="IsCustomized">Whether any override is stored.</param>
/// <param name="CanEdit">Whether the row is currently editable.</param>
public sealed record BatchProductPromptResponseDto(
    Guid RowId,
    string Subject,
    string ArtStyle,
    string MoodTone,
    string NegativeTerms,
    string Instructions,
    string DefaultEffectivePrompt,
    string DefaultBasePrompt,
    string DefaultNiche,
    string DefaultStyleModifiers,
    string EffectivePrompt,
    int CharacterCount,
    bool IsCustomized,
    bool CanEdit);
