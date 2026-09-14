namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// Creates and confirms real PayOS payment links for checkout (UC60 Process Payment).
/// </summary>
/// <remarks>
/// This is an Application-owned abstraction over the PayOS SDK: the SDK's own types never cross
/// into Application, so Application does not depend on the third-party <c>payOS</c> package
/// (only Infrastructure's <c>PayOsGatewayClient</c> implementation does).
/// </remarks>
public interface IPaymentGatewayClient
{
    /// <summary>
    /// Creates a payment link for the given order.
    /// </summary>
    /// <remarks>
    /// The return/cancel redirect URLs are an Infrastructure concern (built from the client
    /// application's base address, the same way <c>EmailService</c> builds its own links) — not
    /// parameters the Application layer needs to supply.
    /// </remarks>
    Task<PaymentLinkResult> CreatePaymentLinkAsync(
        long orderCode,
        int amountVnd,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a raw PayOS webhook request body and extracts its order code and outcome.
    /// </summary>
    /// <param name="rawJsonBody">The exact bytes PayOS posted, as a string.</param>
    Task<WebhookVerificationResult> VerifyWebhookAsync(
        string rawJsonBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-checks a payment link's status directly with PayOS.
    /// </summary>
    /// <remarks>
    /// Used to reconcile a checkout that has stayed "pending" for a while, in case the webhook
    /// was lost (a real risk in local dev behind an ngrok tunnel).
    /// </remarks>
    Task<PaymentLinkStatusResult> GetPaymentLinkStatusAsync(
        long orderCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the webhook URL PayOS should call. A one-time (or on-URL-change) setup step —
    /// not part of any Seller-facing use case.
    /// </summary>
    Task ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a payment link the Seller backed out of (clicked Back or closed the checkout modal
    /// before paying), so it cannot still be paid later and stops showing as outstanding on PayOS.
    /// </summary>
    Task CancelPaymentLinkAsync(
        long orderCode,
        string cancellationReason,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of creating a PayOS payment link.
/// </summary>
public sealed record PaymentLinkResult(string PaymentLinkId, string CheckoutUrl, string QrCode);

/// <summary>
/// The result of verifying a PayOS webhook payload.
/// </summary>
/// <param name="IsValid">False when the signature does not check out; the payload must be ignored.</param>
/// <param name="Success">PayOS's own success flag for the transaction.</param>
public sealed record WebhookVerificationResult(bool IsValid, long OrderCode, bool Success);

/// <summary>
/// The result of re-checking a payment link's status with PayOS.
/// </summary>
public sealed record PaymentLinkStatusResult(bool IsPaid, bool IsFailed);
