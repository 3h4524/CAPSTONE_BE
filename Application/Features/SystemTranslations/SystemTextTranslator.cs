using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;

namespace APCS.Application.Features.SystemTranslations;

public static class SystemTextTranslator
{
    public static async Task<IReadOnlyDictionary<string, string>?> TranslateToVietnameseAsync(
        IReadOnlyDictionary<string, string> fields,
        ITranslationService translationService,
        CancellationToken cancellationToken = default)
    {
        var items = fields.Select(field => new TranslateTextItemDto(field.Key, field.Value)).ToArray();
        var result = await translationService.TranslateAsync(
            new TranslateTextsRequestDto("en", SystemTranslationDocument.Vietnamese, items),
            cancellationToken);

        if (!result.IsSuccess || result.Value.Items.Any(item => !item.IsTranslated))
            return null;

        return result.Value.Items.ToDictionary(item => item.Key, item => item.Text, StringComparer.Ordinal);
    }
}
