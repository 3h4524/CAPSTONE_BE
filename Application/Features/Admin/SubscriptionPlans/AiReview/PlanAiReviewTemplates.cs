using System.Globalization;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>
/// The backend's own sentences for the parts every review must have, written from the figures when
/// the model's version breaks a rule, so a review is never lost over one sentence.
/// </summary>
internal static class PlanAiReviewTemplates
{
    /// <summary>"Starter has 113 subscribers and 27,517,313 VND in MRR."</summary>
    public static (string Text, IReadOnlyList<string> Metrics) Summary(PlanAiReviewPlanInput plan)
    {
        var metrics = plan.Metrics;
        return metrics.MrrEndVnd is { } mrr
            ? ($"{plan.Name} has {Count(metrics.SubscribersNow)} subscribers and {Vnd(mrr)} VND in MRR.", ["subscribers_now", "mrr_end_vnd"])
            : ($"{plan.Name} has {Count(metrics.SubscribersNow)} subscribers.", ["subscribers_now"]);
    }

    /// <summary>"Retention is 65.3%, 35 customers stopped paying and 49 new customers joined."</summary>
    public static (string Text, IReadOnlyList<string> Metrics) ScoreReason(PlanAiReviewPlanInput plan)
    {
        if (!plan.EnoughData) return ("Too few subscribers to score.", []);

        var metrics = plan.Metrics;
        var parts = new List<string>();
        var cited = new List<string>();
        if (metrics.RetentionRatePct is { } retention)
        {
            parts.Add($"retention is {Pct(retention)}%");
            cited.Add("retention_rate_pct");
        }
        if (metrics.CustomersStoppedPaying is { } stopped)
        {
            parts.Add($"{Count(stopped)} customers stopped paying");
            cited.Add("customers_stopped_paying");
        }
        if (metrics.CustomersNew is { } joined)
        {
            parts.Add($"{Count(joined)} new customers joined");
            cited.Add("customers_new");
        }

        return parts.Count == 0 ? ("The score weighs new customers against those who stopped paying.", []) : (Sentence(parts), cited);
    }

    /// <summary>"MRR rose 10.9% and the plan gained 12 customers on balance."</summary>
    public static (string Text, IReadOnlyList<string> Metrics) Outlook(PlanAiReviewPlanInput plan)
    {
        var metrics = plan.Metrics;
        var parts = new List<string>();
        var cited = new List<string>();
        if (metrics.MrrChangePct is { } change)
        {
            parts.Add(change == 0 ? "MRR held steady" : $"MRR {(change > 0 ? "rose" : "fell")} {Pct(Math.Abs(change))}%");
            cited.Add("mrr_change_pct");
        }
        if (metrics.CustomersNetChange is { } net)
        {
            parts.Add(net == 0
                ? "the plan kept the same number of customers"
                : $"the plan {(net > 0 ? "gained" : "lost")} {Count(Math.Abs(net))} customers on balance");
            cited.Add("customers_net_change");
        }

        return parts.Count == 0 ? ("There is no MRR at the start of the period to compare with.", []) : (Sentence(parts), cited);
    }

    /// <summary>"12 of 39 finished trials bought a plan, and 5 trials are still running."</summary>
    public static (string Text, IReadOnlyList<string> Metrics) Overall(PlanAiReviewInput input)
    {
        var trial = input.Trial;
        if (trial.Finished == 0)
            return trial.StillRunning > 0
                ? ($"No trial has finished yet; {Count(trial.StillRunning)} trials are still running.", ["trial.still_running"])
                : ("No trials finished in this period.", []);

        return ($"{Count(trial.Converted)} of {Count(trial.Finished)} finished trials bought a plan, and {Count(trial.StillRunning)} trials are still running.",
            ["trial.converted", "trial.finished", "trial.still_running"]);
    }

    private static string Sentence(List<string> parts)
    {
        var text = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    private static string Vnd(decimal value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static string Pct(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);
}
