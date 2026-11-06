using System.Text.Json;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues.Dtos;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>
/// The figures sent to the model, serialized in snake_case. Every figure is computed by the
/// backend; unavailable ones stay null and are never sent as zero.
/// </summary>
/// <remarks>
/// Units are in the names: <c>_vnd</c> is whole đồng, <c>_pct</c> is 0–100, <c>_ratio</c> is a
/// plain ratio and anything else is a count. The prompt's glossary defines each one.
/// </remarks>
/// <param name="UnderusedBelowPct">A feature whose adoption_pct is below this is underused (Plan Insights' one rule).</param>
public sealed record PlanAiReviewInput(
    PlanAiReviewPeriod Period,
    string Currency,
    PlanAiReviewTotals Totals,
    IReadOnlyList<PlanAiReviewPlanInput> Plans,
    PlanAiReviewTrialInput Trial,
    decimal UnderusedBelowPct = 30m);

/// <summary>
/// MRR for all plans together: each field is the sum of the plans' own, so the model never adds
/// plans up and the two levels always agree. Null when any plan's figure is unknown.
/// </summary>
/// <param name="MrrPlanChangesVnd">
/// Net MRR from customers who changed plan or price: the plans' moved-in plus expansion, minus
/// moved-out and contraction. A move from one plan to a dearer one adds here.
/// </param>
public sealed record PlanAiReviewTotals(
    decimal? MrrStartVnd,
    decimal? MrrNewVnd,
    decimal? MrrPlanChangesVnd,
    decimal? MrrChurnVnd,
    decimal? MrrEndVnd);

public sealed record PlanAiReviewPeriod(DateOnly From, DateOnly To, int Days);

/// <param name="Key">The plan's tier slug, the identifier the model must answer with.</param>
/// <param name="EnoughData">The plan has enough subscribers to be scored (Plan Insights' minimum sample).</param>
/// <param name="UpsellPlan">The plan <c>upsell_candidates</c> is measured against; null when there is none.</param>
/// <param name="PlanBelow">The next cheaper paid plan, that <c>price_ratio_to_plan_below</c> compares with.</param>
/// <param name="Outlook">improving, stable or declining, set by the backend; the model explains it.</param>
public sealed record PlanAiReviewPlanInput(
    string Key,
    string Name,
    bool EnoughData,
    string? UpsellPlan,
    string? PlanBelow,
    PlanAiReviewPlanMetrics Metrics,
    PlanAiReviewChurnProfile ChurnProfile,
    IReadOnlyList<PlanAiReviewFeatureInput> Features,
    string Outlook = "stable");

/// <param name="MrrChangePct">MRR at the end against the start, in percent; null with none at the start.</param>
/// <param name="CustomersNetChange">Customers at the end minus at the start.</param>
public sealed record PlanAiReviewPlanMetrics(
    decimal MonthlyPriceVnd,
    int SubscribersNow,
    decimal? MrrStartVnd,
    decimal? MrrNewVnd,
    decimal? MrrMovedInVnd,
    decimal? MrrExpansionVnd,
    decimal? MrrContractionVnd,
    decimal? MrrMovedOutVnd,
    decimal? MrrChurnVnd,
    decimal? MrrEndVnd,
    int? CustomersStart,
    int? CustomersStayed,
    int? CustomersSwitchedOut,
    int? CustomersStoppedPaying,
    int? CustomersNew,
    int? CustomersSwitchedIn,
    int? CustomersEnd,
    decimal? RetentionRatePct,
    int UpsellCandidates,
    decimal? MedianDesignImagesInPeriod,
    decimal? MedianMockupsInPeriod,
    decimal? PriceRatioToPlanBelow,
    decimal? MrrChangePct = null,
    int? CustomersNetChange = null);

/// <param name="EnoughData">Both the customers who stayed and those who left reach the backend's minimum group.</param>
/// <param name="CoreFeatures">The plan's two most used features among those who stayed, by name.</param>
/// <param name="LeaveGroups">Every customer who left in exactly one group (see <see cref="PlanChurnSignals"/>).</param>
public sealed record PlanAiReviewChurnProfile(
    bool EnoughData,
    decimal? LeftInFirstMonthPct,
    decimal? UsageChangeBeforeLeavingPct,
    IReadOnlyList<string> CoreFeatures,
    IReadOnlyList<PlanAiReviewLeaveGroup> LeaveGroups);

/// <param name="Left">Customers who left in the group.</param>
/// <param name="LeftPct">Their share of everyone who left; the groups add up to 100.</param>
/// <param name="StayedPct">Share of those who stayed who match the same group.</param>
/// <param name="Notable">The backend found it clearly more common among those who left.</param>
public sealed record PlanAiReviewLeaveGroup(string Key, int Left, decimal? LeftPct, decimal? StayedPct, bool Notable);

/// <param name="UsedByCount">Current subscribers who used it: the people behind <c>adoption_pct</c>.</param>
/// <param name="MedianUsesInPeriod">Times the median subscriber who used it did so over the whole period.</param>
/// <param name="DailyCap">The plan's daily safety cap on the feature; null when it has none.</param>
/// <param name="UsersHitDailyCap">Subscribers who reached the cap on at least one day.</param>
/// <param name="HitDailyCapPct">The same as a share of the plan's current subscribers.</param>
/// <param name="Flags">What the backend found notable (<see cref="PlanAiReviewRules.FeatureFlags"/>); the model writes only about these.</param>
public sealed record PlanAiReviewFeatureInput(
    string Key,
    string Name,
    decimal AdoptionPct,
    decimal? UsedByStayedPct,
    decimal? UsedByChurnedPct,
    decimal? UsageChangeBeforeLeavingPct,
    decimal? FailureRateStayedPct,
    decimal? FailureRateChurnedPct,
    int? UsedByCount = null,
    decimal? MedianUsesInPeriod = null,
    int? DailyCap = null,
    int? UsersHitDailyCap = null,
    decimal? HitDailyCapPct = null,
    IReadOnlyList<string>? Flags = null);

/// <param name="ConversionRatePct">Converted as a share of converted plus <paramref name="ReturnedToFree"/>; running trials are left out.</param>
/// <param name="StillRunning">Trials with no outcome yet, in neither part of the rate.</param>
/// <param name="ReturnedToFree">Trials that expired or were cancelled without buying.</param>
/// <param name="Finished">Converted plus returned to Free: the trials the rate is a share of.</param>
public sealed record PlanAiReviewTrialInput(
    int Started,
    int Converted,
    decimal? ConversionRatePct,
    IReadOnlyList<PlanAiReviewTrialFeature> Features,
    int StillRunning = 0,
    int ReturnedToFree = 0,
    int Finished = 0);

/// <param name="BoughtPct">Share of converted trial users who used it; null when none converted.</param>
/// <param name="NotBoughtPct">Share of the trials that returned to Free whose users used it; null when none did.</param>
public sealed record PlanAiReviewTrialFeature(string Key, string Name, decimal? BoughtPct, decimal? NotBoughtPct);

/// <summary>Builds <see cref="PlanAiReviewInput"/> from the figures the Plan Insights and Revenue pages show.</summary>
internal static class PlanAiReviewInputBuilder
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <param name="insights">Plan Insights for the period.</param>
    /// <param name="byPlan">The Revenue page's per-plan customers and retention, for the same days.</param>
    /// <param name="movement">The Revenue page's MRR movement for all plans (no plan name) and per plan, for the same days.</param>
    public static PlanAiReviewInput Build(
        PlanInsightsDto insights,
        IReadOnlyList<RevenueByPlanDto> byPlan,
        IReadOnlyList<MrrBridgeDto> movement,
        DateOnly from,
        DateOnly to)
    {
        var paid = insights.Plans.Where(plan => !plan.IsTrial).OrderBy(plan => plan.MonthlyPriceVnd).ToList();
        var plans = paid
            .Select((plan, index) =>
            {
                var below = index > 0 ? paid[index - 1] : null;
                var flow = byPlan.FirstOrDefault(item => item.PlanName == plan.Name);
                var bridge = movement.FirstOrDefault(item => item.PlanName == plan.Name);
                var verdict = insights.Verdicts.FirstOrDefault(item => item.Key == plan.Key);
                // Plan Insights shows groups of any size; the review only compares groups that reach the minimum.
                var churn = insights.ChurnSignals.FirstOrDefault(item => item.Key == plan.Key && item.HasEnoughData);
                // A plan the Revenue page has no row for has unknown MRR and customers, not zero.
                var customers = flow?.Customers;

                var metrics = new PlanAiReviewPlanMetrics(
                    plan.MonthlyPriceVnd,
                    plan.Subscribers,
                    Whole(bridge?.Start),
                    Whole(bridge?.New),
                    Whole(bridge?.MovedIn),
                    Whole(bridge?.Expansion),
                    Whole(bridge?.Contraction),
                    Whole(bridge?.MovedOut),
                    Whole(bridge?.Churn),
                    EndOf(bridge),
                    customers?.Start,
                    customers?.Stayed,
                    customers?.SwitchedOut,
                    customers?.StoppedPaying,
                    customers?.New,
                    customers?.SwitchedIn,
                    customers?.End,
                    flow?.RetentionRate,
                    verdict?.UpsellCandidates ?? 0,
                    verdict?.MedianDesignImages,
                    verdict?.MedianMockups,
                    below is { MonthlyPriceVnd: > 0 } ? Math.Round(plan.MonthlyPriceVnd / below.MonthlyPriceVnd, 2) : null);
                metrics = metrics with
                {
                    MrrChangePct = PlanAiReviewRules.MrrChangePct(metrics.MrrStartVnd, metrics.MrrEndVnd),
                    CustomersNetChange = PlanAiReviewRules.CustomersNetChange(metrics.CustomersStart, metrics.CustomersEnd),
                };

                var profile = new PlanAiReviewChurnProfile(
                    churn?.HasEnoughData ?? false,
                    churn?.LeftInFirstMonthRate,
                    churn?.UsageChangeBeforeLeaving,
                    // The core features name what a vague "core features" means, so they are sent even for a small sample.
                    insights.ChurnSignals.FirstOrDefault(item => item.Key == plan.Key)?.CoreFeatures ?? [],
                    (churn?.LeaveGroups ?? [])
                        .Select(group => new PlanAiReviewLeaveGroup(group.Key, group.Left, group.LeftRate, group.StayedRate, group.Notable))
                        .ToList());

                // Only what the plan includes; a feature it lacks has nothing to judge.
                var features = plan.Adoption
                    .Where(feature => feature.IncludedInPlan)
                    .Select(feature =>
                    {
                        var signal = churn?.Features.FirstOrDefault(item => item.Metric == feature.Metric);
                        return new PlanAiReviewFeatureInput(
                            feature.Metric,
                            feature.Label,
                            feature.Rate,
                            signal?.StayedRate,
                            signal?.LeftRate,
                            signal?.UsageChangeBeforeLeaving,
                            signal?.StayedFailureRate,
                            signal?.LeftFailureRate,
                            feature.Users,
                            feature.MedianUses,
                            feature.DailyCap,
                            feature.UsersAtDailyCap,
                            feature.UsersAtDailyCap is int atCap && plan.Subscribers > 0
                                ? Math.Round(atCap * 100m / plan.Subscribers, 1)
                                : null);
                    })
                    .Select(feature => feature with { Flags = PlanAiReviewRules.FeatureFlags(feature, insights.VerdictRules.PromoteBelowRate) })
                    .ToList();

                var outlook = PlanAiReviewRules.Outlook(metrics.MrrStartVnd, metrics.MrrEndVnd, metrics.CustomersNetChange);
                return new PlanAiReviewPlanInput(plan.Key, plan.Name, plan.HasEnoughData, verdict?.UpsellPlan, below?.Name, metrics, profile, features, outlook);
            })
            .ToList();

        var trial = insights.Trial;
        return new PlanAiReviewInput(
            new PlanAiReviewPeriod(from, to, insights.Days),
            "VND",
            Totals(plans.Select(plan => plan.Metrics).ToList()),
            plans,
            new PlanAiReviewTrialInput(
                trial.Started,
                trial.ConvertedToPro + trial.ConvertedToStarter + trial.ConvertedToOther,
                trial.ConversionRate,
                trial.FeatureComparison
                    .Select(feature => new PlanAiReviewTrialFeature(feature.Metric, feature.Label, feature.ConvertedRate, feature.NotConvertedRate))
                    .ToList(),
                trial.Running,
                trial.ReturnedToFree,
                trial.ConvertedToPro + trial.ConvertedToStarter + trial.ConvertedToOther + trial.ReturnedToFree),
            insights.VerdictRules.PromoteBelowRate);
    }

    public static string Serialize(PlanAiReviewInput input) => JsonSerializer.Serialize(input, Json);

    private static decimal? Whole(decimal? vnd) => vnd is { } value ? Math.Round(value) : null;

    // Each movement is rounded to whole đồng on its own, so the end is worked out from the rounded
    // parts: End = Start + New + MovedIn + Expansion - Contraction - MovedOut - Churn then holds
    // exactly, at most 1 đồng from the unrounded end the Revenue page rounds.
    private static decimal? EndOf(MrrBridgeDto? bridge) =>
        bridge is null
            ? null
            : Whole(bridge.Start) + Whole(bridge.New) + Whole(bridge.MovedIn) + Whole(bridge.Expansion)
                - Whole(bridge.Contraction) - Whole(bridge.MovedOut) - Whole(bridge.Churn);

    private static PlanAiReviewTotals Totals(IReadOnlyList<PlanAiReviewPlanMetrics> plans)
    {
        decimal? Sum(Func<PlanAiReviewPlanMetrics, decimal?> figure) =>
            plans.Count > 0 && plans.All(plan => figure(plan) is not null) ? plans.Sum(plan => figure(plan)!.Value) : null;

        return new PlanAiReviewTotals(
            Sum(plan => plan.MrrStartVnd),
            Sum(plan => plan.MrrNewVnd),
            Sum(plan => plan.MrrMovedInVnd + plan.MrrExpansionVnd - plan.MrrMovedOutVnd - plan.MrrContractionVnd),
            Sum(plan => plan.MrrChurnVnd),
            Sum(plan => plan.MrrEndVnd));
    }
}
