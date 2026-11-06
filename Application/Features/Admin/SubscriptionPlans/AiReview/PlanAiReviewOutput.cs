namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>
/// The model's review, as <see cref="PlanAiReviewValidator"/> accepted it. Plans and features are
/// identified by the keys in <see cref="PlanAiReviewInput"/>; every metric reference names a figure
/// that has a value there, and every amount, rate or count a text states is one of the figures its
/// own references name.
/// </summary>
/// <param name="OverallMetrics">
/// The figures <paramref name="Overall"/> states: "totals.&lt;field&gt;", "trial.&lt;field&gt;",
/// "trial.&lt;feature&gt;.&lt;field&gt;" or "&lt;plan&gt;.&lt;metric&gt;".
/// </param>
public sealed record PlanAiReviewOutput(
    IReadOnlyList<PlanAiAssessment> Plans,
    string Overall,
    IReadOnlyList<string> OverallMetrics);

/// <param name="Plan">The plan's key.</param>
/// <param name="Status">"healthy" (score 75–100), "watch" (50–74), "at_risk" (0–49), or "not_enough_data" with no score.</param>
/// <param name="Score">0–100; null exactly when the plan cannot be scored.</param>
/// <param name="ScoreMetrics">The figures <paramref name="ScoreReason"/> states.</param>
/// <param name="SummaryMetrics">The figures <paramref name="Summary"/> states.</param>
/// <param name="ChurnNote">
/// Written by the backend, not the model: why there are no churn reasons when there are too few
/// customers who left to compare. Null when churn was analysed.
/// </param>
public sealed record PlanAiAssessment(
    string Plan,
    string Status,
    int? Score,
    string ScoreReason,
    IReadOnlyList<string> ScoreMetrics,
    string Summary,
    IReadOnlyList<string> SummaryMetrics,
    PlanAiOutlook Outlook,
    IReadOnlyList<PlanAiFinding> Strengths,
    IReadOnlyList<PlanAiFinding> Risks,
    IReadOnlyList<PlanAiFeatureInsight> FeatureInsights,
    IReadOnlyList<PlanAiChurnReason> ChurnReasons,
    string? ChurnNote,
    IReadOnlyList<PlanAiAction> Actions);

/// <param name="Direction">"improving", "stable" or "declining" over the next period if nothing changes.</param>
public sealed record PlanAiOutlook(string Direction, string Reason, IReadOnlyList<string> Metrics);

public sealed record PlanAiFinding(string Text, IReadOnlyList<string> Metrics);

/// <param name="Feature">The feature's key.</param>
/// <param name="Kinds">One or more of "underused", "needs_improvement" and "keeps_customers".</param>
/// <param name="Text">What the figures show, with them.</param>
/// <param name="Suggestion">What an Admin could do; it names no technical cause the figures do not show.</param>
public sealed record PlanAiFeatureInsight(
    string Feature,
    IReadOnlyList<string> Kinds,
    string Text,
    string Suggestion,
    IReadOnlyList<string> Metrics);

/// <summary>
/// A pattern that goes with leaving, kept apart from any explanation of it. A plan's reasons come
/// strongest first; the Revenue page shows them next to the plan's MRR movement.
/// </summary>
/// <param name="Pattern">What goes together with leaving, stated as an observed link.</param>
/// <param name="Evidence">The figures that show it.</param>
/// <param name="Confidence">"high", "medium" or "low".</param>
/// <param name="Hypothesis">A possible explanation, clearly unconfirmed; null when none is offered.</param>
/// <param name="HowToConfirm">What would confirm or rule out <paramref name="Hypothesis"/>; required with it.</param>
public sealed record PlanAiChurnReason(
    string Pattern,
    string Evidence,
    string Confidence,
    IReadOnlyList<string> Metrics,
    string? Hypothesis,
    string? HowToConfirm);

/// <param name="Priority">"high", "medium" or "low".</param>
/// <param name="Kind">
/// "change" (act on what the figures show), "experiment" (try something and measure it; any timing
/// or threshold it names is a choice to test, not a finding) or "investigate" (find out more first).
/// </param>
/// <param name="Metrics">The figures the action responds to.</param>
public sealed record PlanAiAction(string Text, string Priority, string Kind, IReadOnlyList<string> Metrics);
