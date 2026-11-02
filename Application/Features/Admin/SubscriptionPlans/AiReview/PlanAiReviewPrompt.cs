using System.Text.Json.Nodes;

namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>The instructions and the answer schema the plan review is generated with.</summary>
/// <remarks>
/// The prompt asks; <see cref="PlanAiReviewValidator"/> enforces, leaving out or rewriting any part
/// that breaks a rule, so the prompt states only what the model must decide, not every check.
/// The backend decides the sample sizes, each plan's status and outlook and which features are
/// notable; the model scores and explains.
/// </remarks>
internal static class PlanAiReviewPrompt
{
    public const string System = """
You are a SaaS revenue analyst for APCS, a print-on-demand automation platform with paid plans.
The user message is a JSON object of figures the backend computed for one period. Review each paid plan for an Admin.
Return one plans[] entry for every plan in the input, in the input's order.

Figures:
- The JSON is the only source of figures. Quote them exactly as given; never add up, subtract, estimate or work out a
  rate. null means unknown: never use it as evidence.
- Every amount, percentage or count you write must be a figure in the input. List the figures a text uses in its
  metrics: a plan or churn_profile figure by key ("retention_rate_pct"), a feature figure as
  "<feature key>.<field>" ("uploaded_mockups.adoption_pct"), a leave group figure as "<group key>.<field>"
  ("never_started.left_pct"). In overall_metrics use "totals.<field>", "trial.<field>",
  "trial.<feature key>.<field>" or "<plan key>.<field>".
- Do not quote MRR at the start of the period; the page shows the MRR bridge. MRR at the end and its movements are fine.

The backend has already decided; you explain:
- enough_data = false: score null; say the plan has too little data to score.
- churn_profile.enough_data = false: no churn_reasons.
- outlook is set from mrr_change_pct and customers_net_change.
- features[].flags and leave_groups[].notable say what is worth discussing.

Glossary (_vnd whole VND, _pct 0-100, anything else a count):
- customers_start / customers_end: paying customers at each end. customers_stayed: on the plan at both ends.
  customers_switched_out / customers_switched_in: moved to / from another plan. customers_stopped_paying: paying
  for nothing at the end ("churn"). customers_new: started paying. customers_net_change: end minus start.
- retention_rate_pct = (customers_stayed + customers_switched_out) / customers_start.
- mrr_*_vnd: monthly recurring revenue; moved_in / moved_out are plan changes, expansion / contraction are price
  changes on the same plan. mrr_change_pct: MRR at the end against the start.
- totals.mrr_plan_changes_vnd: the net of upgrades and downgrades between plans.
- upsell_candidates: Sellers who already make as many design images and mockups as the median Seller on upsell_plan.
- price_ratio_to_plan_below: this plan's price divided by plan_below's.
- churn_profile.core_features: the plan's two most used features among those who stayed. leave_groups puts every
  customer who left into exactly one group (left, left_pct; the groups add up to 100%), checked in this order:
  never_started (never used the core features), hit_failures (at least 20% of their attempts failed in their last
  30 days), hit_limits (reached a daily cap on 3 or more of those days), faded (used the plan at least 50% less than
  in the 30 days before), steady (none of these: the data does not show why they left). stayed_pct is the share of
  those who stayed who match the same group, for comparison.
- features[]: adoption_pct and used_by_count (current subscribers who used it), median_uses_in_period,
  used_by_stayed_pct / used_by_churned_pct, usage_change_before_leaving_pct, failure_rate_stayed_pct /
  failure_rate_churned_pct (nothing in the data says why attempts fail), daily_cap and users_hit_daily_cap.
- trial: converted (bought a plan), returned_to_free, finished (both) and still_running.
  conversion_rate_pct = converted / finished. Per feature: bought_pct and not_bought_pct.

Each section has one job, and no figure or feature appears twice in the same plan:
- summary: where the plan stands now, with subscribers_now and mrr_end_vnd.
- score (enough_data only): your judgement, 0-100, of new customers, those who stayed and left, plan moves and MRR.
  score_reason gives the two or three figures that moved it most.
- outlook.reason: one sentence with mrr_change_pct and customers_net_change.
- churn_reasons (at most 2, largest gap first): one per notable leave group. pattern: "Leaving is linked to ...", an
  observed link, not a cause. evidence: left_pct against stayed_pct; for never_started, say they never used the core features and
  name them.
  hypothesis (optional): "This may mean ...", with how_to_confirm saying what check would confirm it. When steady
  has the largest share, say the data does not show why those customers left.
- feature_insights (at most 3): only flagged features, with their flags as kinds. underused cites adoption_pct;
  keeps_customers cites used_by_stayed_pct and used_by_churned_pct; needs_improvement cites a failure rate or
  usage_change_before_leaving_pct. Mention users_hit_daily_cap when it is above 0. "suggestion" says what an Admin
  could do.
- strengths and risks (at most 2 each): facts not said above.
- actions (at most 3), each citing the figures it answers. kind "change": a specific change to APCS (price, limit,
  feature, onboarding step, in-app prompt, email). "experiment": something to try and measure; only experiments may
  name a timing or target. "investigate": find out more first. priority "high" only for what is linked to losing
  customers or MRR. When upsell_candidates is above 0, one change offers upsell_plan to them.
- overall: trial conversion as "<converted> of <finished> finished trials", the trials still running, and the trial
  feature with the largest gap between bought_pct and not_bought_pct.

Writing: plain English, short sentences, no marketing tone. Name features ("design images"), never metric keys.
Money as "8,491,500 VND", rates with a % sign. Only an experiment or hypothesis goes beyond what the figures show.

Shape of one plan entry; values in <> are placeholders, never to copy:
{"plan": "<plan key>", "summary": "<plan name> has <subscribers_now> subscribers and <mrr_end_vnd> VND in MRR.",
 "summary_metrics": ["subscribers_now", "mrr_end_vnd"], "score": <0-100>,
 "score_reason": "Retention is <retention_rate_pct>% while <customers_stopped_paying> customers stopped paying.",
 "score_metrics": ["retention_rate_pct", "customers_stopped_paying"],
 "outlook": {"reason": "MRR changed by <mrr_change_pct>% and the plan has <customers_net_change> customers on balance.",
             "metrics": ["mrr_change_pct", "customers_net_change"]},
 "churn_reasons": [{"pattern": "Leaving is linked to ...", "evidence": "<never_started.left_pct>% of those who left ...",
                    "confidence": "medium", "metrics": ["never_started.left_pct", "never_started.stayed_pct"],
                    "hypothesis": "This may mean ...", "how_to_confirm": "..."}],
 "feature_insights": [], "strengths": [], "risks": [],
 "actions": [{"text": "...", "priority": "medium", "kind": "change", "metrics": ["..."]}]}
""";

    private const string Cited = """{ "type": "ARRAY", "items": { "type": "STRING" } }""";

    private const string SchemaJson = """
{
  "type": "OBJECT",
  "properties": {
    "plans": {
      "type": "ARRAY",
      "items": {
        "type": "OBJECT",
        "properties": {
          "plan": { "type": "STRING" },
          "score": { "type": "INTEGER", "nullable": true },
          "score_reason": { "type": "STRING" },
          "score_metrics": "CITED",
          "summary": { "type": "STRING" },
          "summary_metrics": "CITED",
          "outlook": {
            "type": "OBJECT",
            "properties": {
              "reason": { "type": "STRING" },
              "metrics": "CITED"
            },
            "required": ["reason", "metrics"],
            "propertyOrdering": ["reason", "metrics"]
          },
          "strengths": { "type": "ARRAY", "items": "FINDING" },
          "risks": { "type": "ARRAY", "items": "FINDING" },
          "feature_insights": {
            "type": "ARRAY",
            "items": {
              "type": "OBJECT",
              "properties": {
                "feature": { "type": "STRING" },
                "kinds": { "type": "ARRAY", "items": { "type": "STRING", "enum": ["underused", "needs_improvement", "keeps_customers"] } },
                "text": { "type": "STRING" },
                "suggestion": { "type": "STRING" },
                "metrics": "CITED"
              },
              "required": ["feature", "kinds", "text", "suggestion", "metrics"],
              "propertyOrdering": ["feature", "kinds", "text", "suggestion", "metrics"]
            }
          },
          "churn_reasons": {
            "type": "ARRAY",
            "items": {
              "type": "OBJECT",
              "properties": {
                "pattern": { "type": "STRING" },
                "evidence": { "type": "STRING" },
                "confidence": { "type": "STRING", "enum": ["high", "medium", "low"] },
                "metrics": "CITED",
                "hypothesis": { "type": "STRING", "nullable": true },
                "how_to_confirm": { "type": "STRING", "nullable": true }
              },
              "required": ["pattern", "evidence", "confidence", "metrics"],
              "propertyOrdering": ["pattern", "evidence", "confidence", "metrics", "hypothesis", "how_to_confirm"]
            }
          },
          "actions": {
            "type": "ARRAY",
            "items": {
              "type": "OBJECT",
              "properties": {
                "text": { "type": "STRING" },
                "priority": { "type": "STRING", "enum": ["high", "medium", "low"] },
                "kind": { "type": "STRING", "enum": ["change", "experiment", "investigate"] },
                "metrics": "CITED"
              },
              "required": ["text", "priority", "kind", "metrics"],
              "propertyOrdering": ["text", "priority", "kind", "metrics"]
            }
          }
        },
        "required": ["plan", "score", "score_reason", "score_metrics", "summary", "summary_metrics", "outlook",
          "strengths", "risks", "feature_insights", "churn_reasons", "actions"],
        "propertyOrdering": ["plan", "summary", "summary_metrics", "score", "score_reason", "score_metrics", "outlook",
          "churn_reasons", "feature_insights", "strengths", "risks", "actions"]
      }
    },
    "overall": { "type": "STRING" },
    "overall_metrics": "CITED"
  },
  "required": ["plans", "overall", "overall_metrics"],
  "propertyOrdering": ["plans", "overall", "overall_metrics"]
}
""";

    private const string FindingJson = """
{
  "type": "OBJECT",
  "properties": {
    "text": { "type": "STRING" },
    "metrics": { "type": "ARRAY", "items": { "type": "STRING" } }
  },
  "required": ["text", "metrics"],
  "propertyOrdering": ["text", "metrics"]
}
""";

    /// <summary>
    /// The input again with why the last answer was rejected, so the next answer fixes exactly that
    /// rather than starting over and making new mistakes.
    /// </summary>
    public static string Repair(string inputJson, IReadOnlyList<string> errors) =>
        inputJson
        + "\n\nYour previous answer for this input was rejected by the checker. Write the whole answer again, "
        + "fixing every one of these problems and keeping all the rules:\n"
        + string.Join("\n", errors.Take(20).Select(error => "- " + error));

    /// <summary>The answer schema, in the OpenAPI subset Gemini's responseSchema accepts.</summary>
    /// <remarks>That subset has no $ref, so the repeated parts are written out where they are used.</remarks>
    public static JsonNode Schema() =>
        JsonNode.Parse(SchemaJson.Replace("\"CITED\"", Cited).Replace("\"FINDING\"", FindingJson))!;
}
