using System.Text.Json;
using APCS.Application.Abstractions.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace APCS.Api.Controllers;

/// <summary>
/// Receives Ideogram's call that an image is ready, so the request waiting for it asks for the result at once instead
/// of at its next status check.
/// </summary>
/// <remarks>
/// Anonymous: Ideogram calls with no credentials. Its signature is not checked because nothing the call says is used:
/// it only wakes the request on this server that waits for that generation, which then reads the result from Ideogram
/// with its own key (<see cref="IImageReadySignals"/>). A call for any other generation does nothing.
/// </remarks>
[ApiController]
[Route(IImageReadySignals.IdeogramWebhookPath)]
[AllowAnonymous]
[DisableRateLimiting]
public sealed class IdeogramWebhookController(IImageReadySignals signals) : ControllerBase
{
    /// <summary>The header naming the generation, on every call.</summary>
    public const string GenerationIdHeader = "X-Ideogram-Webhook-Generation-Id";

    /// <summary>A call body is a small JSON document (links and prompts); anything bigger is not Ideogram's.</summary>
    private const int MaximumBodyBytes = 256 * 1024;

    /// <summary>Handles one call. Always answers 200: Ideogram calls again after anything else, and would get the same.</summary>
    [HttpPost]
    [RequestSizeLimit(MaximumBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive([FromHeader(Name = GenerationIdHeader)] string? generationId, CancellationToken cancellationToken)
    {
        if (!IsGenerationId(generationId)) generationId = await GenerationIdInBodyAsync(cancellationToken);
        if (IsGenerationId(generationId)) signals.Ready(generationId);
        return Ok();
    }

    private async Task<string> GenerationIdInBodyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            return body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("generation_id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    // Ideogram's ids are URL-safe base64.
    private static bool IsGenerationId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
