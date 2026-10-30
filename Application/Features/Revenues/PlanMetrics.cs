using System;

namespace APCS.Application.Features.Revenues;

/// <summary>Per-plan figures for the Revenue by Plan card.</summary>
internal static class PlanMetrics
{
    /// <summary>Fewer customers than this at the period start make a retention rate too noisy to show.</summary>
    public const int MinimumRetentionSample = 5;

    /// <summary>Average revenue per paying customer; null when nobody paid.</summary>
    public static decimal? Arpu(decimal revenue, int payingCustomers) =>
        payingCustomers > 0 ? Math.Round(revenue / payingCustomers, 2) : null;

    /// <summary>
    /// The share of a plan's customers at the start still paying at the end, in percent. A customer
    /// who moved to another plan is retained; only leaving every paid plan counts as lost. Null below
    /// <see cref="MinimumRetentionSample"/> customers at the start.
    /// </summary>
    public static decimal? RetentionRate(MrrCustomerFlow flow) =>
        flow.Start >= MinimumRetentionSample ? Math.Round(flow.Retained * 100m / flow.Start, 1) : null;
}
