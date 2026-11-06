namespace APCS.Application.Features.Translations.Dtos.Request;

/// <summary>
/// Requests translation for a batch of text fields.
/// </summary>
public sealed record TranslateTextsRequestDto(
    string SourceLocale,
    string TargetLocale,
    IReadOnlyList<TranslateTextItemDto> Items);

public sealed record TranslateTextItemDto(string Key, string Text);
