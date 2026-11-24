using APCS.Application.Features.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace APCS.Api.Controllers;

/// <summary>
/// Receives MiniMax's calls about the AI video scenes the API makes, so a finished clip is fetched at once instead of on the
/// next status check.
/// </summary>
/// <remarks>
/// Anonymous: MiniMax calls with no credentials and signs nothing. Authenticity comes from the signature in the URL, which
/// only this API hands out (<see cref="AiVideoCallbackUrls"/>), and nothing the call says is acted on before the provider is
/// asked again (<see cref="IAiVideoCallbackService"/>).
/// </remarks>
[ApiController]
[Route(AiVideoCallbackUrls.RoutePrefix)]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class MiniMaxCallbackController(IAiVideoCallbackService callbacks) : ControllerBase
{
    /// <summary>A call body is a small JSON document; anything bigger is not MiniMax's.</summary>
    private const int MaximumBodyBytes = 64 * 1024;

    /// <summary>Handles one MiniMax call: its URL check (the challenge) or a change of a scene's task.</summary>
    /// <remarks>
    /// Answers 200 to every call to a URL it handed out, whatever the call said: MiniMax calls again after anything else, and a
    /// call that matched no scene would only match none again. A URL it did not hand out gets 404.
    /// </remarks>
    [HttpPost("{jobId:guid}/{sceneOrder:int}/{signature}")]
    [RequestSizeLimit(MaximumBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receive(Guid jobId, int sceneOrder, string signature, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var result = await callbacks.ReceiveAsync(jobId, sceneOrder, signature, body, cancellationToken);
        return result.Outcome switch
        {
            AiVideoCallbackOutcome.Unknown => NotFound(),
            AiVideoCallbackOutcome.Challenge => Ok(new { challenge = result.Challenge }),
            _ => Ok()
        };
    }
}
