using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// Builds every error the Subscriptions feature can return, so wording and codes stay in one place.
/// </summary>
internal static class SubscriptionErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized(ErrorCodes.Unauthorized, "The request is not authenticated.");

    public static Error PlanNotFound() =>
        Error.NotFound(ErrorCodes.SubscriptionPlanNotFound, "The selected plan could not be found.");

    public static Error AnnualNotAvailable() =>
        // Validation (400), not NotFound/Conflict: the plan exists, the request is just asking
        // for a billing cycle this plan does not offer.
        new(
            ErrorCodes.SubscriptionAnnualNotAvailable,
            "Annual billing is not available for this plan.",
            ErrorType.Validation);

    /// <summary>
    /// BR190 — a Seller with an active paid plan must use Upgrade/Downgrade instead of Buy.
    /// </summary>
    /// <remarks>
    /// Validation (400), not Conflict/Forbidden: like <c>AuthErrors.PasswordIncorrect()</c>, this is
    /// a business-rule-shaped input rejection, deliberately kept off 401/403/409 so it can never
    /// collide with the client's 401-triggered token-refresh retry logic. Message is MSG57's
    /// verbatim text from the SRS message catalogue.
    /// </remarks>
    public static Error AlreadySubscribed() =>
        new(
            ErrorCodes.SubscriptionAlreadySubscribed,
            "You already have an active paid plan. Please use Upgrade or Downgrade instead.",
            ErrorType.Validation);

    public static Error InvoiceNotFound() =>
        Error.NotFound(ErrorCodes.SubscriptionInvoiceNotFound, "The checkout could not be found.");

    /// <summary>
    /// The PayOS API call itself failed (network error, bad credentials, etc.) — distinct from a
    /// payment being declined, which is never synchronous with real PayOS (see the webhook/status
    /// reconciliation flow instead).
    /// </summary>
    public static Error GatewayUnavailable() =>
        Error.Failure(ErrorCodes.PaymentGatewayUnavailable, "Could not start the payment. Please try again.");

    /// <summary>
    /// Upgrade/Downgrade require an active paid plan. This case is modeled as a UI precondition
    /// rather than a specification message, so this wording is APCS's own.
    /// </summary>
    public static Error NoActivePlanToChange() =>
        new(
            ErrorCodes.SubscriptionNoActivePlan,
            "You do not have an active paid plan to change. Please choose a plan to buy instead.",
            ErrorType.Validation);

    public static Error TargetNotHigherTier() =>
        new(
            ErrorCodes.SubscriptionTargetNotHigherTier,
            "The selected plan is not an upgrade. Please choose a higher-tier plan.",
            ErrorType.Validation);

    public static Error TargetNotLowerTier() =>
        new(
            ErrorCodes.SubscriptionTargetNotLowerTier,
            "The selected plan is not a downgrade. Please choose a lower-tier plan.",
            ErrorType.Validation);

    /// <summary>
    /// Cancelling a scheduled downgrade when none is pending. The specification only describes
    /// cancelling an existing schedule, so this wording is APCS's own.
    /// </summary>
    public static Error NoScheduledDowngrade() =>
        new(
            ErrorCodes.SubscriptionNoScheduledDowngrade,
            "There is no scheduled downgrade to cancel.",
            ErrorType.Validation);

    public static Error InvoicePdfNotFound() =>
        Error.NotFound(
            ErrorCodes.SubscriptionInvoiceNotFound,
            "This invoice could not be found. Please refresh and try again.");

    public static Error InvoicePdfGenerationFailed() =>
        Error.Failure(
            ErrorCodes.SubscriptionInvoicePdfGenerationFailed,
            "Unable to generate the invoice PDF. Please try again.");
}
