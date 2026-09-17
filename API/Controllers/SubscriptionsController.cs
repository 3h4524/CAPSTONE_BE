using APCS.Api.Extensions;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides subscription and (real, PayOS-backed) payment endpoints for Sellers.
/// </summary>
[ApiController]
[Route("api/subscriptions")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class SubscriptionsController(ISubscriptionService subscriptionService) : ControllerBase
{
    /// <summary>
    /// Gets the Subscription Page payload (UC58 View Subscription Plan).
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(SubscriptionOverviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var result = await subscriptionService.GetOverviewAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Creates a real PayOS payment link for the selected plan (UC59 Buy Subscription Plan,
    /// UC60 Process Payment). The subscription is not active yet — see
    /// <see cref="GetCheckoutStatus"/> for confirmation.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Checkout(CheckoutRequestDto request, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.InitiateCheckoutAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Gets a pending checkout's current status, for the client to poll while waiting on PayOS.
    /// </summary>
    [HttpGet("checkout/{invoiceId:guid}/status")]
    [ProducesResponseType(typeof(CheckoutStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCheckoutStatus(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.GetCheckoutStatusAsync(invoiceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Cancels a checkout the Seller backed out of before paying (Back button / closing the modal).
    /// </summary>
    [HttpPost("checkout/{invoiceId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelCheckout(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.CancelCheckoutAsync(invoiceId, cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Upgrades the active subscription to a higher-tier plan, applying a prorated credit.
    /// </summary>
    [HttpPost("upgrade")]
    [ProducesResponseType(typeof(UpgradeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upgrade(UpgradeRequestDto request, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.UpgradeAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Schedules the active subscription to move to a lower-tier plan at the next billing cycle.
    /// </summary>
    [HttpPost("downgrade")]
    [ProducesResponseType(typeof(DowngradeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Downgrade(DowngradeRequestDto request, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.DowngradeAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Cancels a previously scheduled downgrade, keeping the Seller on the current plan.
    /// </summary>
    [HttpPost("downgrade/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelDowngrade(CancellationToken cancellationToken)
    {
        var result = await subscriptionService.CancelScheduledDowngradeAsync(cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Downloads an existing invoice as a PDF. Never creates or edits an
    /// invoice — read/export-only.
    /// </summary>
    [HttpGet("invoices/{invoiceId:guid}/download")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.DownloadInvoiceAsync(invoiceId, cancellationToken);

        return result.IsSuccess
            ? File(result.Value.Content, "application/pdf", result.Value.FileName)
            : result.ToActionResult(this);
    }
}
