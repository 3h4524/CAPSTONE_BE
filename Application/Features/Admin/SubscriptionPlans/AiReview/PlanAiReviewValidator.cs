using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>The outcome of checking a review: the review to store, or why there is none.</summary>
/// <param name="Review">The review, with every part that broke a rule removed or rewritten; null when its structure is broken.</param>
/// <param name="Errors">Why there is no review: the answer is not JSON, or a plan is missing, unknown, repeated or unscored.</param>
/// <param name="Dropped">What was removed or rewritten and why, to show where the prompt needs work.</param>
public sealed record PlanAiReviewValidation(PlanAiReviewOutput? Review, IReadOnlyList<string> Errors, IReadOnlyList<string> Dropped)
{
    public bool IsValid => Review is not null && Errors.Count == 0;
}

/// <summary>
/// Checks a model answer against the input it was given before it is stored or shown: valid JSON
/// is not a valid analysis.
/// </summary>
/// <remarks>
/// Only a broken structure rejects the whole review. Any other part that breaks a rule is left out
/// (a strength, risk, feature insight, churn reason or action) or, for the summary, score reason,
/// outlook and overall, written by the backend from the figures instead, so one bad sentence never
/// costs a review. Every reference must name a figure with a value, and every amount, rate or count a
/// text states must be one of the input's figures, so a number the model added up or worked out is
/// caught. The churn note, status and outlook are the backend's, not the model's.
/// </remarks>
internal static partial class PlanAiReviewValidator
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "underused", "needs_improvement", "keeps_customers" };
    public static readonly IReadOnlySet<string> Levels = new HashSet<string> { "high", "medium", "low" };
    public static readonly IReadOnlySet<string> ActionKinds = new HashSet<string> { "change", "experiment", "investigate" };

    public static PlanAiReviewValidation Validate(string answer, PlanAiReviewInput input)
    {
        PlanAiReviewOutput? review;
        try
        {
            review = JsonSerializer.Deserialize<PlanAiReviewOutput>(answer, PlanAiReviewInputBuilder.Json);
        }
        catch (JsonException)
        {
            return Fail("The answer is not valid JSON in the expected shape.");
        }

        if (review is null) return Fail("The answer is empty.");
        if (review.Plans is null) return Fail("plans is missing.");

        var errors = new List<string>();
        var inputs = input.Plans.ToDictionary(plan => plan.Key);
        foreach (var missing in inputs.Keys.Except(review.Plans.Where(plan => plan is not null).Select(plan => plan.Plan)))
            errors.Add($"Plan {missing} is not reviewed.");

        var seen = new HashSet<string>();
        foreach (var plan in review.Plans)
        {
            if (plan is null) errors.Add("A plan entry is empty.");
            else if (plan.Plan is null || !inputs.TryGetValue(plan.Plan, out var planInput)) errors.Add($"Unknown plan {plan.Plan ?? "(none)"}.");
            else if (!seen.Add(plan.Plan)) errors.Add($"Plan {plan.Plan} is reviewed twice.");
            else if (planInput.EnoughData && plan.Score is null) errors.Add($"Plan {plan.Plan}: has enough data but no score.");
        }

        if (errors.Count > 0) return new PlanAiReviewValidation(null, errors, []);

        review = ApplyLimits(ReclassifyChanges(QualifyFeatureMetrics(NormalizeReferences(Tidy(review, input)))));
        var dropped = new List<string>();
        var (planFigures, overallFigures) = Figures(input);
        var anyExpansion = input.Plans.Any(plan => plan.Metrics.MrrExpansionVnd is > 0);

        var overallErrors = new List<string>();
        if (string.IsNullOrWhiteSpace(review.Overall)) overallErrors.Add("overall is missing.");
        else CheckText(review.Overall, review.OverallMetrics, overallFigures, anyExpansion, "overall", overallErrors);
        var (overall, overallMetrics) = overallErrors.Count == 0
            ? (review.Overall!, review.OverallMetrics ?? [])
            : Rewritten(overallErrors, dropped, PlanAiReviewTemplates.Overall(input));

        review = review with
        {
            Overall = overall,
            OverallMetrics = overallMetrics,
            Plans = review.Plans.Select(plan => CheckPlan(plan, inputs[plan.Plan], planFigures[plan.Plan], dropped)).ToList()
        };
        return new PlanAiReviewValidation(WithDecisions(WithChurnNotes(review, input), input), [], dropped);
    }

    private static PlanAiAssessment CheckPlan(
        PlanAiAssessment plan,
        PlanAiReviewPlanInput input,
        IReadOnlyDictionary<string, decimal> figures,
        List<string> dropped)
    {
        var at = $"Plan {plan.Plan}";
        var expansion = input.Metrics.MrrExpansionVnd is > 0;

        // The plan-level flag decides scoring; the backend derives the status from the score.
        var score = plan.Score;
        if (!input.EnoughData && score is not null)
        {
            dropped.Add($"{at}: has too little data but was scored.");
            score = null;
        }
        else if (score is < 0 or > 100)
        {
            dropped.Add($"{at}: score {score} is outside 0-100.");
            score = Math.Clamp(score.Value, 0, 100);
        }

        // The three headline sentences each give their own figures; one that breaks a rule, or repeats
        // a figure an earlier one gave, is written by the backend from the figures instead.
        var given = new HashSet<string>(StringComparer.Ordinal);
        var (summary, summaryMetrics) = Headline(plan.Summary, plan.SummaryMetrics, figures, expansion, $"{at} summary", given, dropped,
            () => PlanAiReviewTemplates.Summary(input));
        var (scoreReason, scoreMetrics) = input.EnoughData
            ? Headline(plan.ScoreReason, plan.ScoreMetrics, figures, expansion, $"{at} score", given, dropped,
                () => PlanAiReviewTemplates.ScoreReason(input))
            : PlanAiReviewTemplates.ScoreReason(input);
        var (outlookReason, outlookMetrics) = Headline(plan.Outlook?.Reason, plan.Outlook?.Metrics, figures, expansion, $"{at} outlook", given, dropped,
            () => PlanAiReviewTemplates.Outlook(input),
            // The backend sets the direction; the reason must rest on the figures that set it.
            (metrics, errors) =>
            {
                if ((figures.ContainsKey("mrr_change_pct") || figures.ContainsKey("customers_net_change"))
                    && !metrics.Any(metric => metric is "mrr_change_pct" or "customers_net_change"))
                    errors.Add($"{at} outlook: does not cite mrr_change_pct or customers_net_change, which set the outlook.");
            });

        var churnReasons = CheckChurn(plan, input, figures, expansion, dropped);
        var featureInsights = CheckFeatures(plan, input, figures, expansion, dropped);
        var strengths = Findings(plan.Strengths, figures, expansion, $"{at} strength", dropped);
        var risks = Findings(plan.Risks, figures, expansion, $"{at} risk", dropped);

        // Each feature is discussed in one place: the first of a churn reason, a feature insight, a
        // strength or a risk keeps it, and a later item about the same feature is left out.
        var discussed = new Dictionary<string, string>(StringComparer.Ordinal);
        churnReasons = OncePerFeature(churnReasons, reason => FeaturesOf(reason.Metrics), "a churn reason", discussed, at, dropped);
        featureInsights = OncePerFeature(featureInsights, insight => [insight.Feature], "a feature insight", discussed, at, dropped);
        strengths = OncePerFeature(strengths, finding => FeaturesOf(finding.Metrics), "a strength", discussed, at, dropped);
        risks = OncePerFeature(risks, finding => FeaturesOf(finding.Metrics), "a risk", discussed, at, dropped);

        return plan with
        {
            Score = score,
            ScoreReason = scoreReason,
            ScoreMetrics = scoreMetrics,
            Summary = summary,
            SummaryMetrics = summaryMetrics,
            Outlook = new PlanAiOutlook(plan.Outlook?.Direction ?? input.Outlook, outlookReason, outlookMetrics),
            ChurnReasons = churnReasons,
            FeatureInsights = featureInsights,
            Strengths = strengths,
            Risks = risks,
            Actions = CheckActions(plan, input, figures, expansion, dropped)
        };
    }

    // A headline sentence as the model wrote it, or the backend's own when it breaks a rule.
    private static (string Text, IReadOnlyList<string> Metrics) Headline(
        string? text,
        IReadOnlyList<string>? metrics,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        string at,
        HashSet<string> given,
        List<string> dropped,
        Func<(string Text, IReadOnlyList<string> Metrics)> template,
        Action<IReadOnlyList<string>, List<string>>? extraCheck = null)
    {
        var errors = new List<string>();
        var cited = (metrics ?? []).Where(metric => metric is not null).ToList();
        if (string.IsNullOrWhiteSpace(text)) errors.Add($"{at}: is missing.");
        else
        {
            CheckText(text, metrics, figures, expansion, at, errors);
            foreach (var repeated in cited.Where(given.Contains).Distinct())
                errors.Add($"{at}: quotes {repeated}, which an earlier sentence already gives; give each figure once.");
            extraCheck?.Invoke(cited, errors);
        }

        var result = errors.Count == 0 ? (text!, (IReadOnlyList<string>)cited) : Rewritten(errors, dropped, template());
        given.UnionWith(result.Item2);
        return result;
    }

    private static (string Text, IReadOnlyList<string> Metrics) Rewritten(
        List<string> errors,
        List<string> dropped,
        (string Text, IReadOnlyList<string> Metrics) template)
    {
        dropped.AddRange(errors.Select(error => $"{error} Written by the backend instead."));
        return template;
    }

    // Strengths and risks: each one that breaks a rule is left out.
    private static IReadOnlyList<PlanAiFinding> Findings(
        IReadOnlyList<PlanAiFinding>? findings,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        string at,
        List<string> dropped) =>
        Keep(findings, dropped, (finding, errors) =>
        {
            if (finding is null || string.IsNullOrWhiteSpace(finding.Text)) errors.Add($"{at}: is empty.");
            else CheckText(finding.Text, finding.Metrics, figures, expansion, at, errors);
        });

    private static IReadOnlyList<PlanAiFeatureInsight> CheckFeatures(
        PlanAiAssessment plan,
        PlanAiReviewPlanInput input,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        List<string> dropped)
    {
        var features = input.Features.ToDictionary(feature => feature.Key);
        return Keep(plan.FeatureInsights, dropped, (insight, errors) =>
        {
            if (insight?.Feature is null || !features.TryGetValue(insight.Feature, out var feature))
            {
                errors.Add($"Plan {plan.Plan}: unknown feature {insight?.Feature ?? "(none)"}.");
                return;
            }

            var at = $"Plan {plan.Plan} feature {insight.Feature}";
            if (insight.Kinds is not { Count: > 0 } || insight.Kinds.Any(kind => kind is null || !Kinds.Contains(kind)))
                errors.Add($"{at}: kinds must be one or more of underused, needs_improvement, keeps_customers.");
            if (string.IsNullOrWhiteSpace(insight.Text) || string.IsNullOrWhiteSpace(insight.Suggestion))
                errors.Add($"{at}: text or suggestion is missing.");

            var metrics = insight.Metrics ?? [];
            CheckText(insight.Text, metrics, figures, expansion, at, errors);

            // A suggestion may aim at a target, but names no unproven cause or untested timing.
            CheckWording(insight.Suggestion, expansion, allowCause: false, $"{at} suggestion", errors);
            if (insight.Suggestion is not null && Timing().Match(insight.Suggestion) is { Success: true } timing)
                errors.Add($"{at} suggestion: names a timing (\"{timing.Value}\"); put timed tests in an experiment action.");

            // A feature is judged only on its own figures.
            foreach (var metric in metrics.Where(metric => metric is not null && !metric.StartsWith(insight.Feature + ".", StringComparison.Ordinal)))
                errors.Add($"{at}: cites {metric}, which is not this feature's figure.");

            var kinds = insight.Kinds ?? [];
            // The backend decides what is notable about a feature; the model only writes about it.
            foreach (var kind in kinds.Where(kind => kind is not null && Kinds.Contains(kind) && !(feature.Flags ?? []).Contains(kind)))
                errors.Add($"{at}: \"{kind}\" is not flagged for this feature (flags: {string.Join(", ", feature.Flags ?? []) switch { "" => "none", var list => list }}).");
            if (kinds.Contains("underused") && !metrics.Contains($"{insight.Feature}.adoption_pct"))
                errors.Add($"{at}: underused without citing its adoption.");
            if (kinds.Contains("keeps_customers")
                && !(metrics.Contains($"{insight.Feature}.used_by_stayed_pct") && metrics.Contains($"{insight.Feature}.used_by_churned_pct")))
                errors.Add($"{at}: keeps_customers without citing its use by those who stayed and those who left.");
            if (kinds.Contains("needs_improvement")
                && !metrics.Any(metric => metric is not null
                    && (metric.EndsWith(".failure_rate_stayed_pct", StringComparison.Ordinal)
                        || metric.EndsWith(".failure_rate_churned_pct", StringComparison.Ordinal)
                        || metric.EndsWith(".usage_change_before_leaving_pct", StringComparison.Ordinal))))
                errors.Add($"{at}: needs_improvement without citing a failure rate or a change before leaving.");
        });
    }

    private static IReadOnlyList<PlanAiChurnReason> CheckChurn(
        PlanAiAssessment plan,
        PlanAiReviewPlanInput input,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        List<string> dropped)
    {
        var reasons = plan.ChurnReasons ?? [];
        if (!input.ChurnProfile.EnoughData)
        {
            if (reasons.Count > 0) dropped.Add($"Plan {plan.Plan}: gives churn reasons without enough customers who left.");
            return [];
        }

        var at = $"Plan {plan.Plan} churn reason";
        return Keep(reasons, dropped, (reason, errors) =>
        {
            if (reason is null || string.IsNullOrWhiteSpace(reason.Pattern) || string.IsNullOrWhiteSpace(reason.Evidence))
            {
                errors.Add($"{at}: pattern or evidence is missing.");
                return;
            }

            if (reason.Confidence is null || !Levels.Contains(reason.Confidence)) errors.Add($"{at}: unknown confidence {reason.Confidence}.");
            if (reason.Metrics is not { Count: > 0 }) errors.Add($"{at}: cites no figures.");
            CheckText(reason.Pattern, reason.Metrics, figures, expansion, at, errors);
            CheckText(reason.Evidence, reason.Metrics, figures, expansion, at, errors);

            // An explanation is allowed only as a labelled, unconfirmed hypothesis with a way to test it
            // (one without is dropped before these checks).
            if (!string.IsNullOrWhiteSpace(reason.Hypothesis))
            {
                CheckNumbers(reason.Hypothesis, reason.Metrics, figures, $"{at} hypothesis", errors);
                CheckWording(reason.Hypothesis, expansion, allowCause: true, $"{at} hypothesis", errors);
                CheckWording(reason.HowToConfirm, expansion, allowCause: true, $"{at} how_to_confirm", errors);
            }
        });
    }

    private static IReadOnlyList<PlanAiAction> CheckActions(
        PlanAiAssessment plan,
        PlanAiReviewPlanInput input,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        List<string> dropped)
    {
        var at = $"Plan {plan.Plan} action";
        return Keep(plan.Actions, dropped, (action, errors) =>
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Text))
            {
                errors.Add($"{at}: an action is empty.");
                return;
            }

            if (action.Priority is null || !Levels.Contains(action.Priority)) errors.Add($"{at}: unknown priority {action.Priority}.");

            // Failures are urgent only when those who left met more of them than those who stayed.
            if (action.Priority == "high")
            {
                foreach (var feature in (action.Metrics ?? [])
                    .Where(metric => metric is not null && metric.Contains(".failure_rate_", StringComparison.Ordinal))
                    .Select(metric => metric[..metric.IndexOf('.')])
                    .Distinct())
                {
                    var figure = input.Features.FirstOrDefault(item => item.Key == feature);
                    if (figure is not { FailureRateChurnedPct: { } left, FailureRateStayedPct: { } stayed } || left <= stayed)
                        errors.Add($"{at}: \"{action.Text}\" is high priority, but {feature} failures are not higher among those who left.");
                }
            }

            if (action.Kind is null || !ActionKinds.Contains(action.Kind)) errors.Add($"{at}: unknown kind {action.Kind}.");
            if (action.Metrics is not { Count: > 0 }) errors.Add($"{at}: \"{action.Text}\" cites no figures.");
            Cite(action.Metrics, figures, at, errors);
            CheckWording(action.Text, expansion, allowCause: false, at, errors);

            // An experiment sets its own targets and timings; any other action only states figures.
            if (action.Kind == "experiment") return;
            CheckNumbers(action.Text, action.Metrics, figures, at, errors);
            if (Timing().Match(action.Text) is { Success: true } timing)
                errors.Add($"{at}: names a timing (\"{timing.Value}\") the figures do not give; mark it an experiment.");
        });
    }

    // The items that pass their checks; each one that does not is left out and noted.
    private static IReadOnlyList<T> Keep<T>(IReadOnlyList<T>? items, List<string> dropped, Action<T, List<string>> check)
    {
        var kept = new List<T>();
        foreach (var item in items ?? [])
        {
            var errors = new List<string>();
            check(item, errors);
            if (errors.Count == 0) kept.Add(item);
            else dropped.AddRange(errors.Select(error => $"{error} Left out."));
        }

        return kept;
    }

    private static IReadOnlyList<T> OncePerFeature<T>(
        IReadOnlyList<T> items,
        Func<T, IEnumerable<string>> featuresOf,
        string place,
        Dictionary<string, string> discussed,
        string at,
        List<string> dropped)
    {
        var kept = new List<T>();
        foreach (var item in items)
        {
            var features = featuresOf(item).Distinct().ToList();
            if (features.FirstOrDefault(discussed.ContainsKey) is { } repeated)
            {
                dropped.Add($"{at}: discusses {repeated} in {discussed[repeated]} and {place}; discuss each feature once. Left out.");
                continue;
            }

            foreach (var feature in features) discussed[feature] = place;
            kept.Add(item);
        }

        return kept;
    }

    private static IEnumerable<string> FeaturesOf(IEnumerable<string>? metrics) =>
        (metrics ?? []).Where(metric => metric is not null && metric.Contains('.')).Select(metric => metric[..metric.IndexOf('.')]);

    // A text that states figures: its references must exist and its numbers must be the input's.
    private static void CheckText(
        string? text,
        IReadOnlyList<string>? metrics,
        IReadOnlyDictionary<string, decimal> figures,
        bool expansion,
        string at,
        List<string> errors)
    {
        Cite(metrics, figures, at, errors);
        CheckNoStart(metrics, at, errors);
        if (string.IsNullOrEmpty(text)) return;
        CheckNumbers(text, metrics, figures, at, errors);
        CheckWording(text, expansion, allowCause: false, at, errors);
        CheckNeverStarted(text, metrics, at, errors);
    }

    // The never_started group are customers who left without ever using the plan's core features:
    // a text built on it must not say they "stopped" using them.
    private static void CheckNeverStarted(string text, IReadOnlyList<string>? metrics, string at, List<string> errors)
    {
        if (!(metrics ?? []).Any(metric => metric is not null && metric.StartsWith(PlanChurnSignals.NeverStarted + ".", StringComparison.Ordinal))) return;
        if (Stopped().Match(text) is { Success: true } stopped)
            errors.Add($"{at}: says \"{stopped.Value}\" for those who never used the plan's core features.");
    }

    // MRR at the start is shown on the page as part of the MRR bridge; quoting it in a sentence invites
    // a bridge that does not add up, so a text may quote the end and the movements but not the start.
    private static void CheckNoStart(IReadOnlyList<string>? metrics, string at, List<string> errors)
    {
        foreach (var metric in (metrics ?? []).Where(metric => metric is not null && metric.EndsWith("mrr_start_vnd", StringComparison.Ordinal)))
            errors.Add($"{at}: quotes {metric}; MRR at the start is shown in the MRR bridge, not in the text.");
    }

    // Every reference must name a figure the input has a value for.
    private static void Cite(IReadOnlyList<string>? metrics, IReadOnlyDictionary<string, decimal> figures, string at, List<string> errors)
    {
        foreach (var metric in metrics ?? [])
            if (metric is null || !figures.ContainsKey(metric))
                errors.Add($"{at}: cites {metric ?? "(none)"}, which is not a figure with a value here.");
    }

    // An amount in VND, a rate in % or a count of customers must be one of the input's figures for that
    // plan (or for the overall), so nothing is added up or worked out (a fall of -53.8 may be written
    // "53.8%"). A true figure the model forgot to list in its metrics is not worth a rewrite.
    private static void CheckNumbers(
        string? text,
        IReadOnlyList<string>? metrics,
        IReadOnlyDictionary<string, decimal> figures,
        string at,
        List<string> errors)
    {
        if (string.IsNullOrEmpty(text)) return;
        var known = figures.Values.SelectMany(value => new[] { value, Math.Abs(value) }).ToHashSet();

        foreach (var match in Money().Matches(text).Concat(Percent().Matches(text)).Concat(Count().Matches(text)))
            if (!known.Contains(Parse(match.Groups[1].Value.Replace(",", ""))))
                errors.Add($"{at}: \"{match.Value}\" is not a figure in the input.");
    }

    // No metric keys, no claimed causes, no technical causes the figures cannot show, no vague "core
    // usage" where a feature can be named, and expansion only when there was some.
    private static void CheckWording(string? text, bool expansion, bool allowCause, string at, List<string> errors)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (MetricKey().Match(text) is { Success: true } key) errors.Add($"{at}: names the metric key {key.Value}.");
        if (!allowCause && Cause().Match(text) is { Success: true } cause)
            errors.Add($"{at}: claims a cause (\"{cause.Value}\") where only a link is shown.");
        if (VagueFeature().Match(text) is { Success: true } vague)
            errors.Add($"{at}: says \"{vague.Value}\"; name the feature instead.");
        if (Technical().Match(text) is { Success: true } technical)
            errors.Add($"{at}: names a technical cause (\"{technical.Value}\") no figure shows.");
        if (!expansion && Expansion().Match(text) is { Success: true } word)
            errors.Add($"{at}: says \"{word.Value}\" with no expansion revenue.");
    }

    /// <summary>
    /// The figures each plan's texts may cite, by reference: plan and churn-profile figures by key,
    /// feature figures as "feature.field"; and those the overall may cite: "totals.field",
    /// "trial.field", "trial.feature.field" and "plan.reference". Only figures with a value count,
    /// and a trial feature both groups never used is not a comparison.
    /// </summary>
    internal static (IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> Plans, IReadOnlyDictionary<string, decimal> Overall) Figures(
        PlanAiReviewInput input)
    {
        var root = JsonNode.Parse(PlanAiReviewInputBuilder.Serialize(input))!;
        var overall = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var plans = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.Ordinal);

        foreach (var plan in root["plans"]!.AsArray())
        {
            var figures = new Dictionary<string, decimal>(StringComparer.Ordinal);
            Add(figures, "", plan!["metrics"]);
            Add(figures, "", plan["churn_profile"]);
            foreach (var group in plan["churn_profile"]!["leave_groups"]?.AsArray() ?? [])
                Add(figures, group!["key"]!.GetValue<string>() + ".", group);
            foreach (var feature in plan["features"]!.AsArray())
                Add(figures, feature!["key"]!.GetValue<string>() + ".", feature);

            var key = plan["key"]!.GetValue<string>();
            plans[key] = figures;
            foreach (var (reference, value) in figures) overall[$"{key}.{reference}"] = value;
        }

        Add(overall, "totals.", root["totals"]);
        Add(overall, "trial.", root["trial"]);
        foreach (var feature in root["trial"]!["features"]!.AsArray())
        {
            var bought = Number(feature!["bought_pct"]);
            var notBought = Number(feature["not_bought_pct"]);
            if (bought is null || notBought is null || (bought == 0 && notBought == 0)) continue;
            Add(overall, $"trial.{feature["key"]!.GetValue<string>()}.", feature);
        }

        return (plans, overall);

        static void Add(Dictionary<string, decimal> figures, string prefix, JsonNode? node)
        {
            foreach (var (name, value) in node!.AsObject())
                if (Number(value) is { } number) figures[prefix + name] = number;
        }

        static decimal? Number(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;
    }

    // The input's section names, which a model sometimes puts in front of a reference.
    private static readonly string[] Sections = ["metrics.", "churn_profile.", "features."];

    // A reference written with its own plan's key or a section name in front ("starter.retention_rate_pct",
    // "churn_profile.never_started.left_pct") still names one figure, so it is read as that figure
    // rather than rejecting a sound review; one that is still unknown is rejected.
    private static PlanAiReviewOutput NormalizeReferences(PlanAiReviewOutput review)
    {
        static IReadOnlyList<string> InPlan(IReadOnlyList<string>? metrics, string? planKey) =>
            (metrics ?? []).Select(metric =>
            {
                if (metric is null) return metric!;
                if (planKey is not null && metric.StartsWith(planKey + ".", StringComparison.Ordinal)) metric = metric[(planKey.Length + 1)..];
                foreach (var section in Sections)
                    if (metric.StartsWith(section, StringComparison.Ordinal)) metric = metric[section.Length..];
                return metric;
            }).ToList();

        static IReadOnlyList<string> Overall(IReadOnlyList<string>? metrics) =>
            (metrics ?? []).Select(metric => metric is null
                ? metric!
                : Sections.Aggregate(metric, (current, section) => current.Replace("." + section, ".", StringComparison.Ordinal)))
            .ToList();

        return review with
        {
            OverallMetrics = Overall(review.OverallMetrics),
            Plans = review.Plans
                .Select(plan => plan is null ? plan! : plan with
                {
                    ScoreMetrics = InPlan(plan.ScoreMetrics, plan.Plan),
                    SummaryMetrics = InPlan(plan.SummaryMetrics, plan.Plan),
                    Outlook = plan.Outlook is null ? plan.Outlook! : plan.Outlook with { Metrics = InPlan(plan.Outlook.Metrics, plan.Plan) },
                    Strengths = plan.Strengths?.Select(item => item is null ? item! : item with { Metrics = InPlan(item.Metrics, plan.Plan) }).ToList()!,
                    Risks = plan.Risks?.Select(item => item is null ? item! : item with { Metrics = InPlan(item.Metrics, plan.Plan) }).ToList()!,
                    FeatureInsights = plan.FeatureInsights?.Select(item => item is null ? item! : item with { Metrics = InPlan(item.Metrics, plan.Plan) }).ToList()!,
                    ChurnReasons = plan.ChurnReasons?.Select(item => item is null ? item! : item with { Metrics = InPlan(item.Metrics, plan.Plan) }).ToList()!,
                    Actions = plan.Actions?.Select(item => item is null ? item! : item with { Metrics = InPlan(item.Metrics, plan.Plan) }).ToList()!,
                })
                .ToList(),
        };
    }

    // Inside a feature insight a bare field such as "adoption_pct" can only mean that feature's own
    // figure, so it is written out in full; a reference that is still unknown is rejected.
    private static PlanAiReviewOutput QualifyFeatureMetrics(PlanAiReviewOutput review) =>
        review with
        {
            Plans = review.Plans
                .Select(plan => plan?.FeatureInsights is null ? plan! : plan with
                {
                    FeatureInsights = plan.FeatureInsights
                        .Select(insight => insight?.Metrics is null || insight.Feature is null ? insight! : insight with
                        {
                            // A null the model sent stays as it is, so Cite reports it.
                            Metrics = insight.Metrics
                                .Select(metric => metric is not null && !metric.Contains('.') ? $"{insight.Feature}.{metric}" : metric!)
                                .ToList()
                        })
                        .ToList()
                })
                .ToList()
        };

    // Reviewing, assessing or asking customers finds things out; it changes nothing in APCS, so
    // such a "change" is shown as what it is rather than rejecting an otherwise sound review.
    private static PlanAiReviewOutput ReclassifyChanges(PlanAiReviewOutput review) =>
        review with
        {
            Plans = review.Plans
                .Select(plan => plan?.Actions is null ? plan! : plan with
                {
                    Actions = plan.Actions
                        .Select(action => action is { Kind: "change", Text: not null } && FindOut().IsMatch(action.Text)
                            ? action with { Kind = "investigate" }
                            : action)
                        .ToList()
                })
                .ToList()
        };

    // Wording the backend can put right itself, so a sound part is not thrown away over a phrase: a
    // vague "core features" becomes the plan's own core features (the two its customers who stayed
    // used most), and a claimed cause outside a hypothesis becomes a plain link.
    private static PlanAiReviewOutput Tidy(PlanAiReviewOutput review, PlanAiReviewInput input)
    {
        var cores = input.Plans.ToDictionary(
            plan => plan.Key,
            plan => string.Join(" and ", plan.ChurnProfile.CoreFeatures.Select(name => name.ToLowerInvariant())));

        return review with
        {
            Overall = Plain(review.Overall, "")!,
            Plans = review.Plans
                .Select(plan =>
                {
                    var core = cores.GetValueOrDefault(plan.Plan, "");
                    return plan with
                    {
                        ScoreReason = Plain(plan.ScoreReason, core)!,
                        Summary = Plain(plan.Summary, core)!,
                        Outlook = plan.Outlook is null ? plan.Outlook! : plan.Outlook with { Reason = Plain(plan.Outlook.Reason, core)! },
                        Strengths = plan.Strengths?.Select(item => item is null ? item! : item with { Text = Plain(item.Text, core)! }).ToList()!,
                        Risks = plan.Risks?.Select(item => item is null ? item! : item with { Text = Plain(item.Text, core)! }).ToList()!,
                        FeatureInsights = plan.FeatureInsights?
                            .Select(item => item is null ? item! : item with { Text = Plain(item.Text, core)!, Suggestion = Plain(item.Suggestion, core)! })
                            .ToList()!,
                        ChurnReasons = plan.ChurnReasons?
                            .Select(item => item is null ? item! : item with
                            {
                                Pattern = Plain(item.Pattern, core)!,
                                Evidence = Plain(item.Evidence, core)!,
                                Hypothesis = Named(item.Hypothesis, core),
                                HowToConfirm = Named(item.HowToConfirm, core),
                            })
                            .ToList()!,
                        Actions = plan.Actions?.Select(item => item is null ? item! : item with { Text = Plain(item.Text, core)! }).ToList()!,
                    };
                })
                .ToList()
        };
    }

    private static string? Named(string? text, string core) =>
        string.IsNullOrEmpty(text) || core.Length == 0 ? text : CoreFeatures().Replace(text, core);

    private static string? Plain(string? text, string core) =>
        string.IsNullOrEmpty(text)
            ? text
            : Cause().Replace(Named(text, core)!, match => char.IsUpper(match.Value[0]) ? "Alongside" : "alongside");

    // Keeps each list to the size the page is designed for; the most urgent actions are kept. A
    // hypothesis with no way to confirm it is dropped: the pattern and its evidence still stand.
    private static PlanAiReviewOutput ApplyLimits(PlanAiReviewOutput review) =>
        review with
        {
            Plans = review.Plans
                .Select(plan => plan is null ? plan! : plan with
                {
                    ChurnReasons = plan.ChurnReasons?
                        .Take(PlanAiReviewRules.MaxChurnReasons)
                        .Select(reason => reason is not null && string.IsNullOrWhiteSpace(reason.HowToConfirm)
                            ? reason with { Hypothesis = null, HowToConfirm = null }
                            : reason)
                        .ToList()!,
                    FeatureInsights = plan.FeatureInsights?.Take(PlanAiReviewRules.MaxFeatureInsights).ToList()!,
                    Strengths = plan.Strengths?.Take(PlanAiReviewRules.MaxFindings).ToList()!,
                    Risks = plan.Risks?.Take(PlanAiReviewRules.MaxFindings).ToList()!,
                    Actions = plan.Actions?
                        .OrderBy(action => action?.Priority switch { "high" => 0, "medium" => 1, _ => 2 })
                        .Take(PlanAiReviewRules.MaxActions)
                        .ToList()!,
                })
                .ToList()
        };

    // The backend's status (from the score) and outlook replace whatever the model sent.
    private static PlanAiReviewOutput WithDecisions(PlanAiReviewOutput review, PlanAiReviewInput input) =>
        review with
        {
            Plans = review.Plans
                .Select(plan =>
                {
                    var planInput = input.Plans.Single(item => item.Key == plan.Plan);
                    return plan with
                    {
                        Status = PlanAiReviewRules.Status(planInput.EnoughData, plan.Score),
                        Outlook = plan.Outlook with { Direction = planInput.Outlook },
                    };
                })
                .ToList()
        };

    // The backend states why churn was not analysed, in the same words every time.
    private static PlanAiReviewOutput WithChurnNotes(PlanAiReviewOutput review, PlanAiReviewInput input) =>
        review with
        {
            Plans = review.Plans
                .Select(plan =>
                {
                    var planInput = input.Plans.Single(item => item.Key == plan.Plan);
                    return plan with { ChurnNote = planInput.ChurnProfile.EnoughData ? null : ChurnNote(planInput) };
                })
                .ToList()
        };

    private static string ChurnNote(PlanAiReviewPlanInput plan) =>
        plan.Metrics.CustomersStoppedPaying is { } left && plan.Metrics.CustomersStayed is { } stayed
            ? $"Churn patterns were not analysed: {left} customers stopped paying and {stayed} stayed in this period, "
                + $"and each group needs at least {Revenues.PlanUsageSignals.MinimumGroup} to compare them reliably."
            : "Churn patterns were not analysed: there is not enough data on customers who stopped paying in this period.";

    private static decimal Parse(string number) => decimal.Parse(number, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static PlanAiReviewValidation Fail(string error) => new(null, [error], []);

    [GeneratedRegex(@"\b[a-z]+(?:_[a-z]+)+\b")]
    private static partial Regex MetricKey();

    [GeneratedRegex(@"(\d[\d,]*(?:\.\d+)?)\s*VND")]
    private static partial Regex Money();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%")]
    private static partial Regex Percent();

    // Counts of people, and of uses such as "a median of 4 mockups" or "a cap of 20 listings".
    [GeneratedRegex(@"\b(\d[\d,]*(?:\.\d+)?)\s+(?:[A-Za-z-]+\s+){0,2}?(?:customers?|subscribers?|sellers?|users?|trials?|upgrades?|accounts?|times|uses|images?|mockups?|videos?|listings?|exports?|keyframes?|designs?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Count();

    [GeneratedRegex(@"\b(review\w*|evaluat\w*|assess\w*|survey\w*|interview\w*|analy[sz]\w*|investigat\w*|examin\w*|explor\w*|outreach|reach(?:ing)? out)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FindOut();

    [GeneratedRegex(@"\b(due to|because of|caused by)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cause();

    [GeneratedRegex(@"\b(time-?outs?|latency|slow(?:ness)?|speeds?|reliab\w*|servers?|bugs?|crash\w*|API)\b")]
    private static partial Regex Technical();

    [GeneratedRegex(@"\b(?:\d+|one|two|three|four|five|six|seven|ten|fourteen|thirty)[\s-]+(?:hours?|days?|weeks?|months?)\b|\bday one\b|\bfirst (?:day|week|login|session)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Timing();

    [GeneratedRegex(@"\bexpan\w*", RegexOptions.IgnoreCase)]
    private static partial Regex Expansion();

    [GeneratedRegex(@"\b(?:core|main|key)\s+(?:usage|use|features?|functions?|tools?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VagueFeature();

    // The plural or "the ..." forms, which read right as the plan's core features; "a core feature" does not.
    [GeneratedRegex(@"(?<!\b(?:a|an)\s+)\b(?:the\s+)?(?:core|main|key)\s+(?:usage|use|features|functions|tools|feature|function|tool)\b(?!\s+(?:is|was|has)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex CoreFeatures();

    // "stopped paying" is how the review names those who left, so it stays allowed.
    [GeneratedRegex(@"\bstop(?:ped|ping|s)?\b(?!\s+paying)", RegexOptions.IgnoreCase)]
    private static partial Regex Stopped();

    [GeneratedRegex(@"\bdesign\s+images?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DesignImage();

    [GeneratedRegex(@"\bmockups?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Mockup();
}
