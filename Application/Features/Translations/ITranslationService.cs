using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Translations;

/// <summary>
/// Provides cached, batch translation for authenticated application content.
/// </summary>
public interface ITranslationService
{
    Task<Result<TranslateTextsResponseDto>> TranslateAsync(
        TranslateTextsRequestDto request,
        CancellationToken cancellationToken = default);
}
