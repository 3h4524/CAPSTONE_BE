namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// The <c>subscriptions.status</c> values the database's <c>chk_subscriptions_status</c> CHECK
/// constraint actually allows: <c>trialing</c>, <c>active</c>, <c>past_due</c>, <c>cancelled</c>,
/// <c>expired</c>. There is no "pending" value in the real schema.
/// </summary>
internal static class SubscriptionStatuses
{
    /// <summary>
    /// A checkout has been created and a PayOS payment link issued, but payment has not been
    /// confirmed yet. Never counts as an active plan (<see cref="Active"/> is the only status
    /// <c>GetActiveWithPlanAsync</c> matches), so a Seller can still buy again while one of these
    /// is outstanding — the closest fit among the allowed values for "not active yet, awaiting
    /// something" (this app has no real trial-period concept).
    /// </summary>
    public const string AwaitingPayment = "trialing";

    public const string Active = "active";

    /// <summary>The Seller explicitly cancelled the checkout before paying.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>PayOS declined or the payment link expired unpaid — not a Seller action.</summary>
    public const string Expired = "expired";
}

/// <summary>
/// The <c>invoices.status</c> values the database's <c>chk_invoices_status</c> CHECK constraint
/// actually allows: <c>draft</c>, <c>issued</c>, <c>paid</c>, <c>overdue</c>, <c>void</c>,
/// <c>refunded</c>. There is no "pending"/"failed" value in the real schema.
/// </summary>
internal static class InvoiceStatuses
{
    /// <summary>An invoice has been created for a checkout awaiting PayOS confirmation.</summary>
    public const string AwaitingPayment = "issued";

    public const string Paid = "paid";

    /// <summary>The checkout was declined, cancelled, or expired before payment completed.</summary>
    public const string Void = "void";
}

/// <summary>
/// The simplified vocabulary the API exposes to the client, independent of the database's real,
/// more granular status columns above. <c>Failed</c> and <c>Cancelled</c> both correspond to the
/// same <see cref="InvoiceStatuses.Void"/> invoice status — which one applies is read off the
/// linked <see cref="SubscriptionStatuses"/> value instead (Expired vs. Cancelled).
/// </summary>
internal static class CheckoutStatusValues
{
    public const string Pending = "pending";
    public const string Paid = "paid";

    /// <summary>PayOS declined the payment, or the link expired unpaid.</summary>
    public const string Failed = "failed";

    /// <summary>The Seller explicitly cancelled the checkout before paying.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Why a checkout stopped being "awaiting payment", for <c>SubscriptionService</c>'s internal
/// finalization logic.
/// </summary>
internal enum CheckoutOutcome
{
    Paid,
    Declined,
    Cancelled
}
