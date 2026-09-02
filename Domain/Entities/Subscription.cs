using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a seller's subscription to a plan.
/// </summary>
public sealed class Subscription : SoftDeletableEntity
{
    private readonly List<Invoice> _invoices = [];

    private Subscription()
    {
    }

    /// <summary>
    /// Gets the subscribing seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the subscribed plan identifier.
    /// </summary>
    public int PlanId { get; private set; }

    /// <summary>
    /// Gets the billing cycle.
    /// </summary>
    public string BillingCycle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the captured monthly price in USD.
    /// </summary>
    public decimal MonthlyPriceUsd { get; private set; }

    /// <summary>
    /// Gets the captured annual price in USD.
    /// </summary>
    public decimal? AnnualPriceUsd { get; private set; }

    /// <summary>
    /// Gets the subscription status.
    /// </summary>
    public string Status { get; private set; } = "active";

    /// <summary>
    /// Gets the subscription start date.
    /// </summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>
    /// Gets the next renewal date.
    /// </summary>
    public DateOnly RenewalDate { get; private set; }

    /// <summary>
    /// Gets the UTC trial expiration timestamp.
    /// </summary>
    public DateTimeOffset? TrialEndsAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC cancellation timestamp.
    /// </summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether automatic renewal is enabled.
    /// </summary>
    public bool AutoRenew { get; private set; } = true;

    /// <summary>
    /// Gets the subscribed plan.
    /// </summary>
    public SubscriptionPlan Plan { get; private set; } = null!;

    /// <summary>
    /// Gets invoices issued for this subscription.
    /// </summary>
    public IReadOnlyCollection<Invoice> Invoices => _invoices.AsReadOnly();
}
