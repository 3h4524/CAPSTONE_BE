namespace APCS.Domain.Entities;

/// <summary>
/// Enables a named feature on a subscription plan.
/// </summary>
/// <remarks>
/// Keyed by (PlanId, FeatureCode), so it carries no surrogate identifier. Replaces the
/// free-form feature JSON blob that previously lived on the plan row.
/// </remarks>
public sealed class PlanFeature
{
    private PlanFeature()
    {
    }

    /// <summary>
    /// Gets the owning plan identifier.
    /// </summary>
    public Guid PlanId { get; private set; }

    /// <summary>
    /// Gets the feature code.
    /// </summary>
    public string FeatureCode { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the feature is enabled.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Gets the optional numeric limit attached to the feature.
    /// </summary>
    public int? LimitValue { get; private set; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the owning plan.
    /// </summary>
    public SubscriptionPlan Plan { get; private set; } = null!;
}
