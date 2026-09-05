using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a user's subscription to a plan.
/// </summary>
public sealed class Subscription : SoftDeletableEntity
{
    private readonly List<Invoice> _invoices = [];

    private Subscription()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the subscribed plan identifier.
    /// </summary>
    public Guid PlanId { get; private set; }

    /// <summary>
    /// Gets the billing cycle.
    /// </summary>
    public string BillingCycle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the agreed monthly price in USD.
    /// </summary>
    public decimal MonthlyPriceUsd { get; private set; }

    /// <summary>
    /// Gets the agreed annual price in USD.
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
    /// Gets the UTC timestamp at which the trial ends.
    /// </summary>
    public DateTimeOffset? TrialEndsAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC cancellation timestamp.
    /// </summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the subscription renews automatically.
    /// </summary>
    public bool AutoRenew { get; private set; } = true;

    /// <summary>
    /// Gets the subscribed plan.
    /// </summary>
    public SubscriptionPlan Plan { get; private set; } = null!;

    /// <summary>
    /// Gets the invoices issued for this subscription.
    /// </summary>
    public IReadOnlyCollection<Invoice> Invoices => _invoices.AsReadOnly();
}
