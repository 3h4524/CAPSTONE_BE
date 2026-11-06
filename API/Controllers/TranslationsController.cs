using APCS.Api.Extensions;
using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Dtos.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Translates dynamic, user-visible content without exposing provider credentials to the browser.
/// </summary>
[ApiController]
[Authorize]
[Route("api/translations")]
public sealed class TranslationsController(ITranslationService translationService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(TranslateTextsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Translate(
        TranslateTextsRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await translationService.TranslateAsync(request, cancellationToken);
        return result.ToActionResult(this);
    }
}
