using APCS.Application.Features.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Receives PayOS payment confirmation webhooks.
/// </summary>
/// <remarks>
/// Deliberately not under <see cref="SubscriptionsController"/>: PayOS calls this with no Seller
/// JWT, so it must be anonymous. Authenticity comes from the SDK's own signature verification
/// (see <see cref="ISubscriptionService.HandlePayOsWebhookAsync"/>), not from ASP.NET auth.
/// </remarks>
[ApiController]
[Route("api/subscriptions/webhooks/payos")]
[AllowAnonymous]
public sealed class PayOsWebhookController(ISubscriptionService subscriptionService) : ControllerBase
{
    /// <summary>
    /// Handles a PayOS webhook delivery.
    /// </summary>
    /// <remarks>
    /// Always responds 200 once the body has been read, whether or not the signature or lookup
    /// succeeded: PayOS retries a webhook that receives a non-2xx response, and an already-invalid
    /// or already-applied notification would only ever fail the same way again.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawJsonBody = await reader.ReadToEndAsync(cancellationToken);

        await subscriptionService.HandlePayOsWebhookAsync(rawJsonBody, cancellationToken);

        return Ok();
    }
}
