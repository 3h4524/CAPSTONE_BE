namespace APCS.Application.Features.Translations.Dtos.Response;

public sealed record TranslateTextsResponseDto(IReadOnlyList<TranslatedTextDto> Items);

public sealed record TranslatedTextDto(
    string Key,
    string Text,
    bool IsTranslated,
    bool IsCached);
