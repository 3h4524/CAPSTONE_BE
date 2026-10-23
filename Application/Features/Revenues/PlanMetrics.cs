using System;
using System.Collections.Generic;
using System.Linq;

namespace APCS.Application.Features.Revenues;

/// <summary>A plan's retention over a period.</summary>
/// <param name="ActiveAtStart">Customers with a live paid subscription to the plan when the period began.</param>
/// <param name="Retained">Of those, customers still holding a live paid subscription (any plan) after it ended.</param>
/// <param name="Rate">Retained / ActiveAtStart in percent; null below <see cref="PlanMetrics.MinimumRetentionSample"/>.</param>
internal sealed record PlanRetention(int ActiveAtStart, int Retained, decimal? Rate);

/// <summary>Per-plan figures for the Revenue by Plan card.</summary>
internal static class PlanMetrics
{
    /// <summary>Fewer customers than this at the period start make a retention rate too noisy to show.</summary>
    public const int MinimumRetentionSample = 5;

    /// <summary>Average revenue per paying customer; null when nobody paid.</summary>
    public static decimal? Arpu(decimal revenue, int payingCustomers) =>
        payingCustomers > 0 ? Math.Round(revenue / payingCustomers, 2) : null;

    /// <summary>
    /// Customer retention for <paramref name="planName"/> from <paramref name="from"/> to the end of
    /// <paramref name="to"/>. A customer who moved to another plan is retained (they are still paying);
    /// only leaving every paid plan counts as lost. "Live" follows <see cref="SubscriptionMetrics.IsLiveAt"/>,
    /// so unpaid checkouts never count.
    /// </summary>
    public static PlanRetention Retention(
        IReadOnlyList<SubscriptionRecord> subscriptions,
        string planName,
        DateOnly from,
        DateOnly to)
    {
        var afterEnd = to.AddDays(1);

        var atStart = subscriptions
            .Where(s => s.PlanName == planName && SubscriptionMetrics.IsLiveAt(s, from))
            .Select(s => s.UserId)
            .ToHashSet();

        var liveAfterEnd = subscriptions
            .Where(s => SubscriptionMetrics.IsLiveAt(s, afterEnd))
            .Select(s => s.UserId)
            .ToHashSet();

        int retained = atStart.Count(liveAfterEnd.Contains);
        decimal? rate = atStart.Count >= MinimumRetentionSample
            ? Math.Round(retained * 100m / atStart.Count, 1)
            : null;

        return new PlanRetention(atStart.Count, retained, rate);
    }
}
