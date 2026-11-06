using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>Something a Seller made, and when.</summary>
internal sealed record SellerUse(Guid UserId, DateTime At);

/// <summary>An attempt to use a feature that can fail, such as an AI call or a video render.</summary>
internal sealed record SellerAttempt(Guid UserId, DateTime At, bool Failed);

/// <summary>A feature, its uses and, when failures are recorded for it, its attempts.</summary>
/// <param name="DailyCap">The plan's daily cap on the feature; null when it has none.</param>
internal sealed record ChurnFeature(
    string Metric,
    string Label,
    bool IncludedInPlan,
    IReadOnlyList<SellerUse> Uses,
    IReadOnlyList<SellerAttempt>? Attempts,
    int? DailyCap = null);

/// <summary>
/// Compares how a plan's customers who stopped paying used each feature with the ones who stayed,
/// for the AI review to explain why customers leave.
/// </summary>
/// <remarks>
/// Those who stayed are measured from <see cref="PlanUsageSignals.UsageLookbackDays"/> days before
/// the period to its end, the window the Revenue page's usage signal uses, so both pages agree.
/// Those who left are measured over the same window, up to the day they left. Groups of any size
/// are compared; <see cref="PlanChurnSignalDto.HasEnoughData"/> says whether both reach the minimum
/// for a reliable comparison, and only then may the AI review draw on them.
/// </remarks>
internal static class PlanChurnSignals
{
    /// <summary>Days in each of the two windows compared before a customer left.</summary>
    public const int WindowDays = 30;

    /// <summary>Days of usage before the period that must be loaded for <see cref="Build"/>.</summary>
    public const int HistoryDays = 2 * WindowDays;

    /// <summary>A failure rate needs at least this many attempts in its group.</summary>
    public const int MinimumAttempts = 20;

    /// <summary>A change before leaving needs at least this many uses in the earlier window.</summary>
    public const int MinimumUses = 10;

    /// <summary>Leaving within this many days of first paying counts as leaving in the first month.</summary>
    public const int FirstMonthDays = 31;

    /// <summary>How many of a plan's most used features count as its core features.</summary>
    public const int CoreFeatureCount = 2;

    /// <summary>A customer met many failures when at least this share of their attempts in their last 30 days failed.</summary>
    public const decimal FailureSharePct = 20m;

    /// <summary>Attempts a customer needs in their last 30 days before their failure share counts.</summary>
    public const int MinimumAttemptsPerCustomer = 5;

    /// <summary>Days in their last 30 on which a customer reached a daily cap to count as hitting limits.</summary>
    public const int CapDays = 3;

    /// <summary>A fall in a customer's uses, last 30 days against the 30 before, from which they faded out.</summary>
    public const decimal FadedChangePct = -50m;

    /// <summary>Uses a customer needs in the earlier 30 days before a fall counts.</summary>
    public const int MinimumEarlierUses = 4;

    /// <summary>Points more of those who left than of those who stayed for a group to be notable.</summary>
    public const decimal NotableGapPoints = 10m;

    /// <summary>Customers who left a group needs to be notable.</summary>
    public const int NotableMinimumLeft = 3;

    public const string NeverStarted = "never_started";
    public const string HitFailures = "hit_failures";
    public const string HitLimits = "hit_limits";
    public const string Faded = "faded";
    public const string Steady = "steady";

    /// <summary>The leave groups in the order a customer is checked against them.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> LeaveGroups =
    [
        (NeverStarted, "Never got started"),
        (HitFailures, "Met many failures"),
        (HitLimits, "Kept hitting limits"),
        (Faded, "Used it less and less"),
        (Steady, "Used it steadily"),
    ];

    /// <param name="from">The first day of the period.</param>
    /// <param name="to">The last day of the period.</param>
    public static PlanChurnSignalDto Build(
        string key,
        string name,
        PlanCohortDto cohort,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ChurnFeature> features)
    {
        var stayed = cohort.Stayed.ToHashSet();
        var left = cohort.Left.ToDictionary(customer => customer.UserId);
        var hasEnoughData = stayed.Count >= PlanUsageSignals.MinimumGroup && left.Count >= PlanUsageSignals.MinimumGroup;

        var windowStart = Start(from.AddDays(-PlanUsageSignals.UsageLookbackDays));
        var periodEnd = Start(to.AddDays(1));
        bool InStayedWindow(Guid userId, DateTime at) => stayed.Contains(userId) && at >= windowStart && at < periodEnd;
        bool InLeftWindow(Guid userId, DateTime at) =>
            left.TryGetValue(userId, out var customer) && at >= windowStart && at < Start(customer.LeftOn);

        var signals = features
            .Select(feature =>
            {
                var stayedUsers = feature.Uses.Where(use => InStayedWindow(use.UserId, use.At)).Select(use => use.UserId).ToHashSet();
                var leftUsers = feature.Uses.Where(use => InLeftWindow(use.UserId, use.At)).Select(use => use.UserId).ToHashSet();
                return new FeatureChurnSignalDto(
                    feature.Metric,
                    feature.Label,
                    feature.IncludedInPlan,
                    Percent(stayedUsers.Count, stayed.Count),
                    Percent(leftUsers.Count, left.Count),
                    ChangeBeforeLeaving(feature.Uses, left),
                    FailureRate(feature.Attempts, attempt => InStayedWindow(attempt.UserId, attempt.At)),
                    FailureRate(feature.Attempts, attempt => InLeftWindow(attempt.UserId, attempt.At)),
                    stayedUsers.Count,
                    leftUsers.Count);
            })
            .ToList();

        // The plan's core features are the two its customers who stayed used most (all uses break a tie),
        // so "never got started" means never using what keeps customers on this plan.
        var core = features
            .Where(feature => feature.IncludedInPlan)
            .Select(feature => (Feature: feature, Uses: feature.Uses.Count(use => InStayedWindow(use.UserId, use.At)), All: feature.Uses.Count))
            .Where(item => item.All > 0)
            .OrderByDescending(item => item.Uses)
            .ThenByDescending(item => item.All)
            .Take(CoreFeatureCount)
            .Select(item => item.Feature)
            .ToList();
        var coreUsers = core
            .SelectMany(feature => feature.Uses)
            .Where(use => InLeftWindow(use.UserId, use.At) || InStayedWindow(use.UserId, use.At))
            .Select(use => use.UserId)
            .ToHashSet();
        var leftInFirstMonth = left.Values.Count(customer => customer.LeftOn.DayNumber - customer.FirstPaidOn.DayNumber <= FirstMonthDays);

        // Each customer's last day: the day they left, or the period's end for those who stayed.
        var ends = left.ToDictionary(item => item.Key, item => Start(item.Value.LeftOn));
        foreach (var userId in stayed) ends[userId] = periodEnd;
        var usesByUser = features
            .SelectMany(feature => feature.Uses.Select(use => (Feature: feature, Use: use)))
            .ToLookup(item => item.Use.UserId);
        var attemptsByUser = features.SelectMany(feature => feature.Attempts ?? []).ToLookup(attempt => attempt.UserId);
        var groupOf = ends.ToDictionary(
            item => item.Key,
            item => coreUsers.Contains(item.Key) ? LeaveGroup(item.Value, usesByUser[item.Key], attemptsByUser[item.Key]) : NeverStarted);
        var groups = LeaveGroups
            .Select(group =>
            {
                var leftCount = left.Keys.Count(userId => groupOf[userId] == group.Key);
                var stayedCount = stayed.Count(userId => groupOf[userId] == group.Key);
                var leftRate = Percent(leftCount, left.Count);
                var stayedRate = Percent(stayedCount, stayed.Count);
                // Notable when it is clearly more common among those who left; steady users show no pattern.
                var notable = group.Key != Steady
                    && leftCount >= NotableMinimumLeft
                    && leftRate - (stayedRate ?? 0m) >= NotableGapPoints;
                return new LeaveGroupDto(group.Key, group.Label, leftCount, leftRate, stayedCount, stayedRate, notable);
            })
            .ToList();

        return new PlanChurnSignalDto(
            key,
            name,
            stayed.Count,
            left.Count,
            PlanUsageSignals.MinimumGroup,
            hasEnoughData,
            Percent(leftInFirstMonth, left.Count),
            Percent(left.Keys.Count(userId => !coreUsers.Contains(userId)), left.Count),
            ChangeBeforeLeaving(features.SelectMany(feature => feature.Uses), left),
            signals,
            core.Select(feature => feature.Label).ToList(),
            groups);
    }

    /// <summary>
    /// The group of a customer who used the core features, checked in this order: met many failures,
    /// kept reaching a daily cap, used the plan much less, or none of these. All over their last 30
    /// days before <paramref name="end"/>, against the 30 before for the fall.
    /// </summary>
    private static string LeaveGroup(
        DateTime end,
        IEnumerable<(ChurnFeature Feature, SellerUse Use)> uses,
        IEnumerable<SellerAttempt> attempts)
    {
        var lastStart = end.AddDays(-WindowDays);
        var lastAttempts = attempts.Where(attempt => attempt.At >= lastStart && attempt.At < end).ToList();
        if (lastAttempts.Count >= MinimumAttemptsPerCustomer
            && lastAttempts.Count(attempt => attempt.Failed) * 100m / lastAttempts.Count >= FailureSharePct)
            return HitFailures;

        var all = uses.ToList();
        var capDays = all
            .Where(item => item.Feature.DailyCap is not null && item.Use.At >= lastStart && item.Use.At < end)
            .GroupBy(item => (item.Feature, item.Use.At.Date))
            .Where(day => day.Count() >= day.Key.Feature.DailyCap!.Value)
            .Select(day => day.Key.Date)
            .Distinct()
            .Count();
        if (capDays >= CapDays) return HitLimits;

        var last = all.Count(item => item.Use.At >= lastStart && item.Use.At < end);
        var before = all.Count(item => item.Use.At >= lastStart.AddDays(-WindowDays) && item.Use.At < lastStart);
        if (before >= MinimumEarlierUses && (last - before) * 100m / before <= FadedChangePct) return Faded;

        return Steady;
    }

    // Everything those who left made in their last 30 days against the 30 days before.
    private static decimal? ChangeBeforeLeaving(IEnumerable<SellerUse> uses, IReadOnlyDictionary<Guid, LeftCustomerDto> left)
    {
        int last = 0, before = 0;
        foreach (var use in uses)
        {
            if (!left.TryGetValue(use.UserId, out var customer)) continue;
            var leftAt = Start(customer.LeftOn);
            if (use.At >= leftAt.AddDays(-WindowDays) && use.At < leftAt) last++;
            else if (use.At >= leftAt.AddDays(-HistoryDays) && use.At < leftAt.AddDays(-WindowDays)) before++;
        }

        return before < MinimumUses ? null : Math.Round((last - before) * 100m / before, 1);
    }

    private static decimal? FailureRate(IReadOnlyList<SellerAttempt>? attempts, Func<SellerAttempt, bool> inGroup)
    {
        if (attempts is null) return null;
        var group = attempts.Where(inGroup).ToList();
        return group.Count < MinimumAttempts ? null : Percent(group.Count(attempt => attempt.Failed), group.Count);
    }

    private static DateTime Start(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static decimal? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(part * 100m / whole, 1);
}
