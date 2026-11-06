namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>
/// What the backend decides for the AI review, so the same figures always give the same labels:
/// each plan's status and outlook, and which features are notable and how. The model only explains them.
/// </summary>
internal static class PlanAiReviewRules
{
    /// <summary>An MRR change of this many percent or more, either way, moves the outlook off stable.</summary>
    public const decimal OutlookMrrChangePct = 5m;

    /// <summary>Points more of those who stayed than of those who left using a feature for it to keep customers.</summary>
    public const decimal KeepsCustomersGapPoints = 10m;

    /// <summary>A failure rate from which a feature needs improvement even when it is not linked to leaving.</summary>
    public const decimal HighFailureRatePct = 10m;

    /// <summary>A fall in how much those who left used a feature before leaving, from which it needs improvement.</summary>
    public const decimal UsageDropPct = -25m;

    public const int MaxChurnReasons = 2;
    public const int MaxFeatureInsights = 3;
    public const int MaxFindings = 2;
    public const int MaxActions = 3;

    public const string Underused = "underused";
    public const string KeepsCustomers = "keeps_customers";
    public const string NeedsImprovement = "needs_improvement";

    /// <summary>MRR at the end against the start, in percent; null when there was none at the start.</summary>
    public static decimal? MrrChangePct(decimal? start, decimal? end) =>
        start is > 0 && end is { } last ? Math.Round((last - start.Value) * 100m / start.Value, 1) : null;

    /// <summary>Customers at the end minus at the start; null when either is unknown.</summary>
    public static int? CustomersNetChange(int? start, int? end) => start is { } first && end is { } last ? last - first : null;

    /// <summary>
    /// Declining when MRR fell by <see cref="OutlookMrrChangePct"/>% or more or the plan lost customers on
    /// balance; improving when MRR grew by that much and it lost none; otherwise stable.
    /// </summary>
    public static string Outlook(decimal? mrrStart, decimal? mrrEnd, int? customersNetChange)
    {
        if (customersNetChange < 0) return "declining";
        if (MrrChangePct(mrrStart, mrrEnd) is not { } change)
            return mrrStart is null or 0 && mrrEnd is > 0 ? "improving" : "stable";
        if (change <= -OutlookMrrChangePct) return "declining";
        return change >= OutlookMrrChangePct ? "improving" : "stable";
    }

    /// <summary>The status band of a score: 75–100 healthy, 50–74 watch, 0–49 at risk.</summary>
    public static string Status(bool enoughData, int? score) => !enoughData || score is not { } value
        ? "not_enough_data"
        : value >= 75 ? "healthy" : value >= 50 ? "watch" : "at_risk";

    /// <summary>What is notable about a feature; the model may write only about these.</summary>
    public static IReadOnlyList<string> FeatureFlags(PlanAiReviewFeatureInput feature, decimal underusedBelow)
    {
        var flags = new List<string>();
        if (feature.AdoptionPct < underusedBelow) flags.Add(Underused);
        if (feature is { UsedByStayedPct: { } stayed, UsedByChurnedPct: { } left } && stayed - left >= KeepsCustomersGapPoints)
            flags.Add(KeepsCustomers);

        var failsMoreForThoseWhoLeft = feature is { FailureRateChurnedPct: { } churnedFailures, FailureRateStayedPct: { } stayedFailures }
            && churnedFailures > stayedFailures;
        var failsOften = feature.FailureRateStayedPct >= HighFailureRatePct || feature.FailureRateChurnedPct >= HighFailureRatePct;
        if (failsMoreForThoseWhoLeft || failsOften || feature.UsageChangeBeforeLeavingPct <= UsageDropPct)
            flags.Add(NeedsImprovement);
        return flags;
    }
}
