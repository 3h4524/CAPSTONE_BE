namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>
/// The Plan Insights tab: how Sellers on each plan use the product, what they cost the platform,
/// how trials convert, and where the pipeline loses them.
/// </summary>
/// <param name="Days">The period every figure covers, ending now.</param>
/// <param name="MinimumSample">
/// Subscribers a plan needs before its adoption, usage and cost figures are trusted.
/// </param>
/// <param name="Verdicts">The paid plans, cheapest first, each judged with its evidence and a suggested action.</param>
/// <param name="VerdictRules">The thresholds the verdicts used, so the page can state them.</param>
/// <param name="ChurnSignals">For each paid plan, how the customers it lost used the product next to those it kept.</param>
public sealed record PlanInsightsDto(
    int Days,
    DateTime GeneratedAt,
    int MinimumSample,
    TrialInsightDto Trial,
    IReadOnlyList<PlanGroupInsightDto> Plans,
    IReadOnlyList<PipelineStageDto> Pipeline,
    IReadOnlyList<UnitCostDto> UnitCosts,
    IReadOnlyList<PlanVerdictDto> Verdicts,
    VerdictRulesDto VerdictRules,
    IReadOnlyList<PlanChurnSignalDto> ChurnSignals);

/// <summary>Trials started in the period and what became of them.</summary>
/// <param name="Running">Trials still running: no outcome yet, so not in the conversion rate.</param>
/// <param name="ReturnedToFree">Trials that expired or were cancelled without buying.</param>
/// <param name="ConversionRate">
/// Converted trials as a share of finished ones (converted plus returned to Free), 0–100; null when
/// none has finished. See <see cref="TrialConversion"/>.
/// </param>
/// <param name="PreviousConversionRate">The same rate, worked out the same way, for the period before.</param>
public sealed record TrialInsightDto(
    int Started,
    int Running,
    int ConvertedToPro,
    int ConvertedToStarter,
    int ConvertedToOther,
    int ReturnedToFree,
    decimal? ConversionRate,
    decimal? PreviousConversionRate,
    IReadOnlyList<TrialFeatureComparisonDto> FeatureComparison,
    IReadOnlyList<TrialConversionByPlanDto>? ConvertedByPlan = null);

/// <summary>How many trials bought a plan, per plan: the breakdown that follows the plans an Admin sets up.</summary>
public sealed record TrialConversionByPlanDto(string Tier, string Name, int Count);

/// <summary>
/// How many converted and not-converted trial users used a feature during their trial.
/// </summary>
/// <param name="ConvertedRate">Share of converted trial users who used it, 0–100.</param>
/// <param name="NotConvertedRate">Share of ended, not-converted trial users who used it, 0–100.</param>
public sealed record TrialFeatureComparisonDto(
    string Metric,
    string Label,
    decimal? ConvertedRate,
    decimal? NotConvertedRate);

/// <summary>One plan's subscribers (or the running trials) and how they use the product.</summary>
/// <param name="Key">The plan's tier slug, or "trial" for running trials.</param>
/// <param name="HasEnoughData">False below <see cref="PlanInsightsDto.MinimumSample"/> subscribers.</param>
/// <param name="MonthlyPriceVnd">The plan's monthly price in whole đồng, as PayOS charges it.</param>
public sealed record PlanGroupInsightDto(
    string Key,
    string Name,
    bool IsTrial,
    int Subscribers,
    bool HasEnoughData,
    decimal MonthlyPriceVnd,
    IReadOnlyList<FeatureAdoptionDto> Adoption,
    PlanCostDto Cost);

/// <summary>Share of a plan's subscribers who used a feature at least once in the period.</summary>
/// <param name="IncludedInPlan">False when the plan does not include the feature at all.</param>
/// <param name="Rate">0–100.</param>
/// <param name="MedianUses">How many times the median Seller who used it did so in the period; null when nobody did.</param>
/// <param name="DailyCap">The plan's daily safety cap on the feature; null when it has none.</param>
/// <param name="UsersAtDailyCap">Subscribers who reached the daily cap on at least one day; null without a cap.</param>
public sealed record FeatureAdoptionDto(
    string Metric,
    string Label,
    bool IncludedInPlan,
    int Users,
    decimal Rate,
    decimal? MedianUses = null,
    int? DailyCap = null,
    int? UsersAtDailyCap = null);

/// <summary>Estimated platform cost against what a plan earns, per subscriber, in VND at the PayOS rate.</summary>
/// <param name="CostShare">Cost per subscriber as a share of the monthly price, 0–100; null for a free trial.</param>
public sealed record PlanCostDto(
    decimal PlatformCostPerSubscriberVnd,
    decimal RevenuePerSubscriberVnd,
    decimal? CostShare);

/// <summary>Products created in the period that reached a workflow step and every step before it.</summary>
/// <param name="IsAvailable">False for a step on the canvas whose executor does not exist yet, such as publishing.</param>
public sealed record PipelineStageDto(string Key, string Label, int Products, bool IsAvailable = true);

/// <summary>The thresholds behind <see cref="PlanVerdictDto"/>; see <c>PlanVerdictOptions</c>.</summary>
public sealed record VerdictRulesDto(
    decimal PremiumPriceRatio,
    decimal LowRetentionRate,
    decimal PromoteBelowRate);

/// <summary>One paid plan judged by fixed rules, with the evidence and the action it suggests.</summary>
/// <param name="Verdict">"overpriced", "retention_risk", "upsell", "healthy" or "not_enough_data".</param>
/// <param name="Tone">"warning", "danger", "info", "success" or "neutral".</param>
/// <param name="Evidence">Up to three sentences with the figures behind the verdict.</param>
/// <param name="Action">What an Admin could do; null when there is too little data.</param>
/// <param name="UpsellCandidates">Sellers already using as much as a typical Seller on <paramref name="UpsellPlan"/>.</param>
/// <param name="UpsellPlan">The plan <paramref name="UpsellCandidates"/> is measured against; null when there is none.</param>
/// <param name="MedianDesignImages">Design images the typical (median) Seller on the plan made in the whole period; null with too little data.</param>
/// <param name="MedianMockups">Rendered mockups the typical Seller made in the whole period; null with too little data.</param>
public sealed record PlanVerdictDto(
    string Key,
    string Name,
    string Verdict,
    string VerdictLabel,
    string Tone,
    IReadOnlyList<string> Evidence,
    string? Action,
    int UpsellCandidates,
    string? UpsellPlan = null,
    decimal? MedianDesignImages = null,
    decimal? MedianMockups = null);

/// <summary>A unit cost the platform-cost estimate assumes, in VND.</summary>
public sealed record UnitCostDto(string Key, string Label, decimal CostVnd, bool IsMeasured);

/// <summary>
/// How a paid plan's customers who stopped paying used the product, next to the ones who stayed.
/// </summary>
/// <remarks>
/// Both groups are the plan's customers at the start of the period (see
/// <see cref="Revenues.Dtos.PlanCohortDto"/>). The figures show what went with leaving, not why
/// customers left. Groups of any size are compared; a rate is null only when its group is empty.
/// </remarks>
/// <param name="Key">The plan's tier slug.</param>
/// <param name="Stayed">Customers still on the plan at the end.</param>
/// <param name="Left">Customers paying for nothing at the end.</param>
/// <param name="MinimumGroup">Customers each group needs for the comparison to be reliable.</param>
/// <param name="HasEnoughData">Both groups reach <paramref name="MinimumGroup"/>; below it the figures are a small sample.</param>
/// <param name="LeftInFirstMonthRate">Share of those who left who did so within a month of first paying, 0–100.</param>
/// <param name="LeftWithoutCoreUseRate">Share of those who left who never used the plan's <paramref name="CoreFeatures"/>, 0–100.</param>
/// <param name="UsageChangeBeforeLeaving">
/// Change in everything those who left made, from the 30 days before their last 30 to their last 30, in percent.
/// </param>
/// <param name="CoreFeatures">The plan's core features: the two its customers who stayed used most, most used first.</param>
/// <param name="LeaveGroups">
/// Every customer who left, and for comparison every one who stayed, in exactly one group, in the
/// order they are checked; each group's share of those who left adds up to 100.
/// </param>
public sealed record PlanChurnSignalDto(
    string Key,
    string Name,
    int Stayed,
    int Left,
    int MinimumGroup,
    bool HasEnoughData,
    decimal? LeftInFirstMonthRate,
    decimal? LeftWithoutCoreUseRate,
    decimal? UsageChangeBeforeLeaving,
    IReadOnlyList<FeatureChurnSignalDto> Features,
    IReadOnlyList<string> CoreFeatures,
    IReadOnlyList<LeaveGroupDto> LeaveGroups);

/// <summary>What a plan's customers did before they left, as one group per customer.</summary>
/// <param name="Key">never_started, hit_failures, hit_limits, faded or steady.</param>
/// <param name="Left">Customers who left in this group.</param>
/// <param name="LeftRate">Their share of everyone who left, 0–100; null when nobody left.</param>
/// <param name="Stayed">Customers who stayed who match the same group, for comparison.</param>
/// <param name="StayedRate">Their share of everyone who stayed, 0–100; null when nobody stayed.</param>
/// <param name="Notable">Clearly more common among those who left than those who stayed.</param>
public sealed record LeaveGroupDto(
    string Key,
    string Label,
    int Left,
    decimal? LeftRate,
    int Stayed,
    decimal? StayedRate,
    bool Notable);

/// <summary>One feature's use by a plan's customers who stayed and who left.</summary>
/// <param name="StayedRate">Share of those who stayed who used it, 0–100.</param>
/// <param name="LeftRate">Share of those who left who used it before leaving, 0–100.</param>
/// <param name="UsageChangeBeforeLeaving">
/// Change in how much those who left used it, from the 30 days before their last 30 to their last
/// 30, in percent; null with fewer than 10 uses in the earlier 30 days.
/// </param>
/// <param name="StayedFailureRate">Share of the attempts by those who stayed that failed, 0–100; null when not measured or too few attempts.</param>
/// <param name="LeftFailureRate">The same for those who left.</param>
/// <param name="StayedUsers">Customers who stayed and used it: the people behind <paramref name="StayedRate"/>.</param>
/// <param name="LeftUsers">Customers who left and used it before leaving.</param>
public sealed record FeatureChurnSignalDto(
    string Metric,
    string Label,
    bool IncludedInPlan,
    decimal? StayedRate,
    decimal? LeftRate,
    decimal? UsageChangeBeforeLeaving,
    decimal? StayedFailureRate,
    decimal? LeftFailureRate,
    int StayedUsers = 0,
    int LeftUsers = 0);
