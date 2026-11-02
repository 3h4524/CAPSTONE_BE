namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// The thresholds the Plan Insights verdicts and trial advice use, bound from the
/// <c>PlanInsights</c> configuration section (for example <c>PlanInsights__LowRetentionRate=50</c>)
/// so the team can tune them to APCS's own data without a code change.
/// </summary>
public sealed class PlanVerdictOptions
{
    public const string SectionName = "PlanInsights";

    /// <summary>Subscribers a plan needs before its figures are trusted and it is judged.</summary>
    public int MinimumSample { get; set; } = 20;

    /// <summary>A plan at least this many times the price of the one below is expected to be used more.</summary>
    public decimal PremiumPriceRatio { get; set; } = 1.5m;

    /// <summary>Retention below this percent, and the lowest of all plans, makes a plan a retention risk.</summary>
    public decimal LowRetentionRate { get; set; } = 60m;

    /// <summary>
    /// A feature the plan includes that fewer Sellers than this percent use is underused and worth
    /// promoting. The one rule for "underused" everywhere: the verdicts, the low-adoption alerts, the
    /// Feature adoption table and the AI review's "underused" label.
    /// </summary>
    public decimal PromoteBelowRate { get; set; } = 30m;

    /// <summary>Whether every threshold is in its meaningful range.</summary>
    public bool IsValid() =>
        MinimumSample >= 1
        && PremiumPriceRatio > 1m
        && LowRetentionRate is > 0m and <= 100m
        && PromoteBelowRate is > 0m and <= 100m;
}
