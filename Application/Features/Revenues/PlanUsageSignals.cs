using System;
using System.Collections.Generic;
using System.Linq;

namespace APCS.Application.Features.Revenues;

/// <summary>A feature that the customers a plan kept used far more than the ones it lost.</summary>
/// <param name="StayedRate">Share of the customers who stayed that used it, 0–100.</param>
/// <param name="LeftRate">Share of the customers who left that used it, 0–100.</param>
internal sealed record PlanUsageGap(string Feature, string Label, int Stayed, int Left, decimal StayedRate, decimal LeftRate);

/// <summary>
/// Compares what a plan's customers who left used with what the ones who stayed used, to point an
/// Admin at the feature most worth showing to new customers.
/// </summary>
/// <remarks>
/// A correlation, not a cause: customers who never find a feature tend to leave, or customers about
/// to leave stop using it. Both groups are judged over the period plus the
/// <see cref="UsageLookbackDays"/> days before it, so a customer who left early in the period still
/// has a window of use.
/// </remarks>
internal static class PlanUsageSignals
{
    /// <summary>Each group needs at least this many customers before their usage is compared.</summary>
    public const int MinimumGroup = 10;

    /// <summary>Days before the period whose usage also counts.</summary>
    public const int UsageLookbackDays = 30;

    /// <summary>A gap of at least this many percentage points raises a financial alert.</summary>
    public const decimal AlertGapPoints = 30m;

    /// <summary>
    /// Splits the customers on <paramref name="planName"/> when the period began into those still on
    /// it at its end and those who stopped paying altogether. Customers who moved to another plan are
    /// in neither group.
    /// </summary>
    public static (HashSet<Guid> Stayed, HashSet<Guid> Left) Groups(
        IReadOnlyList<MrrSubscription> subscriptions,
        DateOnly from,
        DateOnly to,
        string planName)
    {
        var start = MrrLedger.ValueAt(subscriptions, from.AddDays(-1));
        var end = MrrLedger.ValueAt(subscriptions, to);

        var onPlanAtStart = start.Keys.Where(key => key.PlanName == planName).Select(key => key.UserId).ToHashSet();
        var onPlanAtEnd = end.Keys.Where(key => key.PlanName == planName).Select(key => key.UserId).ToHashSet();
        var payingAtEnd = end.Keys.Select(key => key.UserId).ToHashSet();

        var stayed = onPlanAtStart.Where(onPlanAtEnd.Contains).ToHashSet();
        var left = onPlanAtStart.Where(userId => !payingAtEnd.Contains(userId)).ToHashSet();
        return (stayed, left);
    }

    /// <summary>
    /// Gets when a customer first paid and the first day they no longer paid for anything; the
    /// latter is null while any of their subscriptions still earns MRR on <paramref name="today"/>.
    /// </summary>
    public static (DateOnly FirstPaidOn, DateOnly? LeftOn) Tenure(
        IReadOnlyList<MrrSubscription> subscriptions,
        Guid userId,
        DateOnly today)
    {
        var own = subscriptions.Where(subscription => subscription.UserId == userId).ToList();
        var firstPaidOn = own.Min(subscription => subscription.FirstPaidOn);
        if (own.Any(subscription => MrrLedger.IsLiveAt(subscription, today)))
        {
            return (firstPaidOn, null);
        }

        // A subscription stops earning on its end date or on its unpaid renewal date, whichever is first.
        var leftOn = own.Max(subscription =>
            subscription.EndedOn is { } ended && ended < subscription.RenewalDate ? ended : subscription.RenewalDate);
        return (firstPaidOn, leftOn);
    }

    /// <summary>
    /// Gets the feature with the largest positive gap between the two groups; null when either group
    /// is below <see cref="MinimumGroup"/> or no feature was used more by those who stayed.
    /// </summary>
    /// <param name="usage">Each feature's key, label and the customers who used it.</param>
    public static PlanUsageGap? LargestGap(
        IReadOnlySet<Guid> stayed,
        IReadOnlySet<Guid> left,
        IEnumerable<(string Feature, string Label, IReadOnlySet<Guid> Users)> usage)
    {
        if (stayed.Count < MinimumGroup || left.Count < MinimumGroup)
        {
            return null;
        }

        return usage
            .Select(feature => new PlanUsageGap(
                feature.Feature,
                feature.Label,
                stayed.Count,
                left.Count,
                Rate(stayed, feature.Users),
                Rate(left, feature.Users)))
            .Where(gap => gap.StayedRate > gap.LeftRate)
            .OrderByDescending(gap => gap.StayedRate - gap.LeftRate)
            .ThenBy(gap => gap.Feature, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static decimal Rate(IReadOnlySet<Guid> group, IReadOnlySet<Guid> users) =>
        Math.Round(group.Count(users.Contains) * 100m / group.Count, 1);
}
