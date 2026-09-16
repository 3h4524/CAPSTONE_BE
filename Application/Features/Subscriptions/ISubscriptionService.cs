using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.Features.Subscriptions.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Subscriptions;

/// <summary>
/// Provides the Subscriptions feature's use cases.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Gets the Subscription Page payload: current plan, usage quotas, available plans, and
    /// recent invoices (UC58 View Subscription Plan).
    /// </summary>
    Task<Result<SubscriptionOverviewResponseDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a real PayOS payment link for the selected plan (UC59 Buy Subscription Plan,
    /// UC60 Process Payment). The subscription/invoice are created in a "pending" state; they
    /// only activate once PayOS confirms the payment (via webhook or status reconciliation).
    /// </summary>
    Task<Result<CheckoutResponseDto>> InitiateCheckoutAsync(
        CheckoutRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a checkout the Seller is waiting on, reconciling with PayOS
    /// directly when it has stayed "pending" long enough that a lost webhook is a real concern.
    /// </summary>
    Task<Result<CheckoutStatusResponseDto>> GetCheckoutStatusAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a verified PayOS webhook notification: activates the subscription on success, or
    /// marks the checkout failed. Idempotent — a checkout that is no longer "pending" is a no-op.
    /// </summary>
    Task HandlePayOsWebhookAsync(
        string rawJsonBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a checkout the Seller backed out of (Back button or closing the modal) before
    /// paying, so it does not linger as an orphaned pending checkout. Idempotent — a checkout
    /// that already resolved is a no-op.
    /// </summary>
    Task<Result> CancelCheckoutAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades the Seller's active paid subscription to a higher-tier plan, applying a prorated
    /// credit for the unused days of the current cycle (UC62).
    /// </summary>
    /// <remarks>
    /// Activates immediately when the prorated credit fully covers the new plan's price;
    /// otherwise the response carries a PayOS checkout the client completes via UC60, exactly
    /// like <see cref="InitiateCheckoutAsync"/>.
    /// </remarks>
    Task<Result<UpgradeResponseDto>> UpgradeAsync(
        UpgradeRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules the Seller's active paid subscription to move to a lower-tier plan at the start
    /// of the next billing cycle (UC63). No payment is collected and the current plan's limits
    /// stay in force until the effective date (BR114-BR116).
    /// </summary>
    Task<Result<DowngradeResponseDto>> DowngradeAsync(
        DowngradeRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a previously scheduled downgrade (BR118), keeping the Seller on the current plan
    /// indefinitely.
    /// </summary>
    Task<Result> CancelScheduledDowngradeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders an existing invoice as a downloadable PDF (UC61). Never creates or edits an
    /// invoice (BR120, BR124) — read/export-only.
    /// </summary>
    Task<Result<InvoiceFileDto>> DownloadInvoiceAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default);
}
