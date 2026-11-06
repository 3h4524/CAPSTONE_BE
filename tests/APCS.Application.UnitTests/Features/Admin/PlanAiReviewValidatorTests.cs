using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanAiReviewValidatorTests
{
    [TestMethod]
    public void Validate_AReviewThatFollowsTheInput_IsValid()
    {
        var result = PlanAiReviewValidator.Validate(Json(PlanAiReviewFixtures.ValidReview()), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void Validate_APlanWithEnoughDataButTooFewWhoLeft_IsScoredAndGetsTheBackendsChurnNote()
    {
        var result = PlanAiReviewValidator.Validate(Json(PlanAiReviewFixtures.ValidReview()), PlanAiReviewFixtures.Input());

        var studio = result.Review!.Plans.Single(plan => plan.Plan == "studio");
        studio.Score.Should().NotBeNull();
        studio.ChurnReasons.Should().BeEmpty();
        studio.ChurnNote.Should().Be(
            "Churn patterns were not analysed: 35 customers stopped paying and 64 stayed in this period, and each group needs at least 10 to compare them reliably.");
        result.Review.Plans.Single(plan => plan.Plan == "starter").ChurnNote.Should().BeNull();
    }

    [TestMethod]
    public void Validate_TextThatIsNotJson_IsRejected()
    {
        var result = PlanAiReviewValidator.Validate("{\"plans\": [", PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("not valid JSON");
    }

    [TestMethod]
    public void Validate_AScoreAbove100_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Score = 101 });

        Errors(review).Should().Contain(error => error.Contains("score 101 is outside 0-100"));
    }

    [TestMethod]
    public void Validate_StatusAndOutlook_AreTheBackends()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Score = 80,
            Status = "at_risk",
            Outlook = plan.Outlook with { Direction = "declining" }
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        var starter = result.Review!.Plans.Single(plan => plan.Plan == "starter");
        starter.Status.Should().Be("healthy", "80 is in the 75-100 band");
        starter.Outlook.Direction.Should().Be("improving", "MRR rose 10.9% and no customers were lost on balance");
        result.Review.Plans.Single(plan => plan.Plan == "vip").Status.Should().Be("not_enough_data");
    }

    [TestMethod]
    public void Validate_AScoreForAPlanWithTooLittleData_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "vip", plan => plan with { Score = 40, Status = "at_risk" });

        Errors(review).Should().Contain(error => error.Contains("vip: has too little data but was scored"));
    }

    [TestMethod]
    public void Validate_ChurnReasonsWithoutEnoughCustomersWhoLeft_AreRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "studio", plan => plan with
        {
            ChurnReasons = [new PlanAiChurnReason("Leaving is linked to the price.", "Studio costs more.", "low", ["monthly_price_vnd"], null, null)]
        });

        Errors(review).Should().Contain(error => error.Contains("studio: gives churn reasons without enough customers who left"));
    }

    [TestMethod]
    public void Validate_AHypothesisWithoutAWayToConfirmIt_IsDroppedAndThePatternKept()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ChurnReasons = [plan.ChurnReasons[0] with { Hypothesis = "This may mean new Sellers never got started.", HowToConfirm = null }]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        var reason = result.Review!.Plans.Single(plan => plan.Plan == "starter").ChurnReasons.Single();
        reason.Pattern.Should().Be("Leaving is linked to never using design images or rendered mockups.");
        reason.Hypothesis.Should().BeNull();
    }

    [TestMethod]
    public void Schema_AsksForEachObjectsFieldsInTheOrderTheyAreThoughtThrough()
    {
        // Without propertyOrdering Gemini writes fields alphabetically: how_to_confirm before hypothesis.
        using var schema = JsonDocument.Parse(PlanAiReviewPrompt.Schema().ToJsonString());
        var plan = schema.RootElement.GetProperty("properties").GetProperty("plans").GetProperty("items");
        var reason = plan.GetProperty("properties").GetProperty("churn_reasons").GetProperty("items");

        reason.GetProperty("propertyOrdering").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("pattern", "evidence", "confidence", "metrics", "hypothesis", "how_to_confirm");
        plan.GetProperty("propertyOrdering").EnumerateArray().Select(item => item.GetString()).Last().Should().Be("actions");
    }

    [TestMethod]
    public void Validate_AnUnknownPlan_IsRejected()
    {
        var review = PlanAiReviewFixtures.ValidReview();
        review = review with { Plans = [.. review.Plans, review.Plans[0] with { Plan = "enterprise" }] };

        Errors(review).Should().Contain("Unknown plan enterprise.");
    }

    [TestMethod]
    public void Validate_AnUnknownFeature_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("ai_videos", ["underused"], "Few use it.", "Show it.", ["ai_videos.adoption_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("unknown feature ai_videos"));
    }

    [TestMethod]
    public void Validate_AnUnknownMetric_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Risks = [new PlanAiFinding("Customers are unhappy.", ["customer_satisfaction_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("cites customer_satisfaction_pct"));
    }

    [TestMethod]
    public void Validate_AFeatureJudgedOnAnotherFeaturesFigures_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("uploaded_mockups", ["underused"], "Only 22.1% use it.", "Show it.",
                ["uploaded_mockups.adoption_pct", "design_images.adoption_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("cites design_images.adoption_pct, which is not this feature's figure"));
    }

    [TestMethod]
    public void Validate_CitingAFigureThatIsNull_IsRejected()
    {
        // Studio's comparison with those who left is null: there were too few of them.
        var review = Change(PlanAiReviewFixtures.ValidReview(), "studio", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("design_images", ["keeps_customers"], "Stayers use it.", "Show it.",
                ["design_images.used_by_stayed_pct", "design_images.used_by_churned_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("cites design_images.used_by_stayed_pct, which is not a figure with a value"));
    }

    [TestMethod]
    public void Validate_KeepsCustomersWithoutBothComparisons_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("design_images", ["keeps_customers"], "76.6% of those who stayed used it.", "Show it.",
                ["design_images.used_by_stayed_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("keeps_customers without citing"));
    }

    [TestMethod]
    public void Validate_ABareFieldInAFeatureInsight_IsReadAsThatFeaturesFigure()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("uploaded_mockups", ["underused"], "Only 22.1% use it.", "Show it.", ["adoption_pct"])]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeTrue();
        result.Review!.Plans.Single(plan => plan.Plan == "starter").FeatureInsights.Single().Metrics
            .Should().Equal("uploaded_mockups.adoption_pct");
    }

    [TestMethod]
    public void Validate_ABareFieldOutsideAFeatureInsight_IsStillRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Risks = [new PlanAiFinding("Few use uploaded mockups.", ["adoption_pct"])]
        });

        Errors(review).Should().Contain(error => error.Contains("cites adoption_pct"));
    }

    [TestMethod]
    public void Validate_ATrueFigureTheMetricsDoNotList_IsAcceptedButOneTheInputLacksIsNot()
    {
        // 49 new customers is a real figure the score reason forgot to cite; 48 is no figure at all.
        var forgot = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ScoreReason = "Retention is 65.3% and 49 new customers joined.",
            ScoreMetrics = ["retention_rate_pct"]
        });
        PlanAiReviewValidator.Validate(Json(forgot), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();

        var madeUp = Change(forgot, "starter", plan => plan with { ScoreReason = "Retention is 65.3% and 48 new customers joined." });
        Errors(madeUp).Should().Contain("Plan starter score: \"48 new customers\" is not a figure in the input.");
    }

    [TestMethod]
    public void Validate_AnAmountOrRateTheModelWorkedOut_IsRejected()
    {
        // 24,810,375 is Starter's cited start; 142,286,355 (a sum off by one) and 22.6% (a growth rate) are in no figure.
        var review = PlanAiReviewFixtures.ValidReview() with
        {
            Overall = "MRR rose from 142,286,355 VND, up 22.6%, with 24,810,375 VND on Starter at the start.",
            OverallMetrics = ["totals.mrr_start_vnd", "starter.mrr_start_vnd"]
        };

        var errors = Errors(review);

        errors.Should().Contain("overall: \"142,286,355 VND\" is not a figure in the input.");
        errors.Should().Contain("overall: \"22.6%\" is not a figure in the input.");
        errors.Should().NotContain(error => error.Contains("24,810,375"));
    }

    [TestMethod]
    public void Validate_CitingATrialFeatureNeitherGroupUsed_IsRejected()
    {
        var review = PlanAiReviewFixtures.ValidReview() with
        {
            OverallMetrics = [.. PlanAiReviewFixtures.ValidReview().OverallMetrics, "trial.exports.bought_pct"]
        };

        Errors(review).Should().Contain(error => error.Contains("cites trial.exports.bought_pct"));
    }

    [TestMethod]
    public void Validate_ACauseOutsideAHypothesis_IsReworded_AndExpansionWithoutAnyIsRejected()
    {
        var cause = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ScoreReason = "Due to 35 customers who stopped paying, retention is 65.3%."
        });
        var result = PlanAiReviewValidator.Validate(Json(cause), PlanAiReviewFixtures.Input());
        result.Errors.Should().BeEmpty();
        result.Review!.Plans.Single(plan => plan.Plan == "starter").ScoreReason
            .Should().Be("Alongside 35 customers who stopped paying, retention is 65.3%.");

        var expanded = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Summary = "Revenue expanded." });
        Errors(expanded).Should().Contain(error => error.Contains("says \"expanded\" with no expansion revenue"));
    }

    [TestMethod]
    public void Validate_ATechnicalCauseNoFigureShows_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [plan.FeatureInsights[0] with { Suggestion = "Fix the API timeout in design generation." }]
        });

        Errors(review).Should().Contain(error => error.Contains("names a technical cause (\"API\")"));
    }

    [TestMethod]
    public void Validate_ATimingInAChangeAction_IsRejectedButAllowedInAnExperiment()
    {
        var timed = new PlanAiAction("Email Sellers with no design after 7 days.", "high", "change", ["never_started.left_pct"]);
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Actions = [timed] });

        Errors(review).Should().Contain(error => error.Contains("names a timing (\"7 days\")"));

        var experiment = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Actions = [timed with { Text = "Test emailing Sellers with no design after 7 days, aiming above 50% use.", Kind = "experiment" }]
        });
        PlanAiReviewValidator.Validate(Json(experiment), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_AFallQuotedWithoutItsSign_IsAccepted()
    {
        // The input has a usage change of -8.1; "fell 8.1%" quotes it.
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Summary = "Design images use fell 8.1% before customers left.",
            SummaryMetrics = ["design_images.usage_change_before_leaving_pct"]
        });

        PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_AHighPriorityFailureActionWhenThoseWhoLeftFailedLess_IsRejected()
    {
        // Starter's design images fail for 4.6% of those who stayed and 2.7% of those who left.
        var action = new PlanAiAction("Investigate why 4.6% of design image attempts fail.", "high", "investigate",
            ["design_images.failure_rate_stayed_pct"]);
        var high = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Actions = [action] });

        Errors(high).Should().Contain(error => error.Contains("is high priority, but design_images failures are not higher among those who left"));

        var medium = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Actions = [action with { Priority = "medium" }] });
        PlanAiReviewValidator.Validate(Json(medium), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_AChangeThatOnlyFindsThingsOut_IsShownAsInvestigate()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Actions = [new PlanAiAction("Review the setup guides with those who left.", "medium", "change", ["never_started.left_pct"])]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeTrue();
        result.Review!.Plans.Single(plan => plan.Plan == "starter").Actions.Single().Kind.Should().Be("investigate");
    }

    [TestMethod]
    public void Validate_UseCountsAndDailyCaps_MustBeTheCitedFigures()
    {
        // Kept customers, not underused: 79.6% is above the page's 30% rule.
        var insight = new PlanAiFeatureInsight("design_images", ["keeps_customers"],
            "90 subscribers used it, a median of 6 images each; 3 subscribers hit the daily cap of 50 images (2.7%).", "Show it.",
            ["design_images.used_by_count", "design_images.adoption_pct", "design_images.median_uses_in_period",
             "design_images.users_hit_daily_cap", "design_images.daily_cap", "design_images.hit_daily_cap_pct",
             "design_images.used_by_stayed_pct", "design_images.used_by_churned_pct"]);
        var cited = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { FeatureInsights = [insight] });

        PlanAiReviewValidator.Validate(Json(cited), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();

        var made = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [insight with { Text = "90 subscribers used it, a median of 7 images each." }]
        });
        Errors(made).Should().Contain(error => error.Contains("\"7 images\" is not a figure in the input"));
    }

    [TestMethod]
    public void Build_SendsEachFeaturesUsersMedianAndDailyCap()
    {
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m));
        insights = insights with
        {
            Plans = [insights.Plans[0] with { Adoption = [new FeatureAdoptionDto("mockups", "Rendered mockups", true, 2, 66.7m, 12m, 40, 1)] }]
        };

        var feature = PlanAiReviewInputBuilder.Build(insights, [], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29)).Plans.Single().Features.Single();

        feature.UsedByCount.Should().Be(2);
        feature.MedianUsesInPeriod.Should().Be(12m);
        feature.DailyCap.Should().Be(40);
        feature.UsersHitDailyCap.Should().Be(1);
        feature.HitDailyCapPct.Should().Be(33.3m, "1 of the plan's 3 subscribers");
    }

    [TestMethod]
    public void Validate_MrrAtTheStart_IsLeftToTheMrrBridge()
    {
        var overall = PlanAiReviewFixtures.ValidReview() with
        {
            Overall = "MRR rose from 142,286,354 VND to 165,989,959 VND.",
            OverallMetrics = ["totals.mrr_start_vnd", "totals.mrr_end_vnd"]
        };
        Errors(overall).Should().Contain("overall: quotes totals.mrr_start_vnd; MRR at the start is shown in the MRR bridge, not in the text.");

        var plan = Change(PlanAiReviewFixtures.ValidReview(), "starter", item => item with
        {
            Summary = "MRR went from 24,810,375 VND to 27,517,313 VND.",
            SummaryMetrics = ["mrr_start_vnd", "mrr_end_vnd"]
        });
        Errors(plan).Should().Contain(error => error.StartsWith("Plan starter summary: quotes mrr_start_vnd"));
    }

    [TestMethod]
    public void Validate_AFigureInMoreThanOneOfSummaryScoreAndOutlook_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Summary = "Starter keeps 65.3% of its customers.",
            SummaryMetrics = ["retention_rate_pct"]
        });

        Errors(review).Should().Contain("Plan starter score: quotes retention_rate_pct, which an earlier sentence already gives; give each figure once.");
    }

    [TestMethod]
    public void Validate_AFeatureInAChurnReasonAndAFeatureInsight_IsRejected()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ChurnReasons = [plan.ChurnReasons[0] with
            {
                Evidence = "48.6% of those who left made design images, against 76.6% of those who stayed.",
                Metrics = ["design_images.used_by_churned_pct", "design_images.used_by_stayed_pct"]
            }]
        });

        Errors(review).Should().Contain("Plan starter: discusses design_images in a churn reason and a feature insight; discuss each feature once.");
    }

    [TestMethod]
    public void Validate_ListsLongerThanThePageShows_AreCutWithTheMostUrgentActionsKept()
    {
        var action = new PlanAiAction("Add a setup checklist for making a first design and mockup.", "low", "change", ["never_started.left_pct"]);
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Strengths = [.. plan.Strengths, .. plan.Strengths, .. plan.Strengths],
            Actions = [action, action, action, action with { Priority = "high" }]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        var starter = result.Review!.Plans.Single(plan => plan.Plan == "starter");
        starter.Strengths.Should().HaveCount(2);
        starter.Actions.Select(item => item.Priority).Should().Equal("high", "low", "low");
    }

    [TestMethod]
    public void Validate_AKindTheBackendDidNotFlag_IsRejected()
    {
        // Design images is used by 79.6%: not underused under the 30% rule the page uses.
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            FeatureInsights = [new PlanAiFeatureInsight("design_images", ["underused"], "Only 79.6% use it.", "Show it.", ["design_images.adoption_pct"])]
        });

        Errors(review).Should().Contain("Plan starter feature design_images: \"underused\" is not flagged for this feature (flags: keeps_customers).");
    }

    [TestMethod]
    public void Build_SendsThePagesUnderusedThreshold()
    {
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m)) with
        {
            VerdictRules = new VerdictRulesDto(1.5m, 60m, 25m)
        };

        PlanAiReviewInputBuilder.Build(insights, [], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29))
            .UnderusedBelowPct.Should().Be(25m);
    }

    [TestMethod]
    public void Validate_ThoseWhoNeverStarted_AreNotSaidToHaveStopped()
    {
        // The group never used the core features at all: "stopped" misreads it.
        var stopped = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ChurnReasons = [plan.ChurnReasons[0] with
            {
                Pattern = "Leaving is linked to stopping use prior to churn.",
                Evidence = "51.4% of customers who left had made nothing before leaving.",
            }]
        });

        var errors = Errors(stopped);

        errors.Should().Contain("Plan starter churn reason: says \"stopping\" for those who never used the plan's core features.");
    }

    [TestMethod]
    public void Validate_VagueCoreFeatures_AreNamedAsThePlansOwnCoreFeatures()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ChurnReasons = [plan.ChurnReasons[0] with
            {
                Pattern = "Leaving is linked to never using the core features.",
                Evidence = "51.4% of those who left never used core features.",
                HowToConfirm = "Check whether those who left opened the core tool."
            }]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        var reason = result.Review!.Plans.Single(plan => plan.Plan == "starter").ChurnReasons.Single();
        // Starter's core features in the input are design images and rendered mockups.
        reason.Pattern.Should().Be("Leaving is linked to never using design images and rendered mockups.");
        reason.Evidence.Should().Be("51.4% of those who left never used design images and rendered mockups.");
        reason.HowToConfirm.Should().Be("Check whether those who left opened design images and rendered mockups.");
    }

    [TestMethod]
    public void Validate_StoppedPaying_IsStillHowThoseWhoLeftAreNamed()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ChurnReasons = [plan.ChurnReasons[0] with
            {
                Evidence = "51.4% of customers who stopped paying never made a design image or a mockup.",
            }]
        });

        PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input()).Errors.Should().BeEmpty();
    }

    [TestMethod]
    public void Validate_AReferenceWithItsPlanOrSectionInFront_IsReadAsThatFigure()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ScoreMetrics = ["starter.retention_rate_pct", "metrics.customers_stopped_paying"],
            ChurnReasons = [plan.ChurnReasons[0] with { Metrics = ["churn_profile.never_started.left_pct", "never_started.stayed_pct"] }]
        }) with
        {
            OverallMetrics = ["trial.conversion_rate_pct", "trial.features.ai_videos.bought_pct", "trial.ai_videos.not_bought_pct"]
        };

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.Errors.Should().BeEmpty();
        var starter = result.Review!.Plans.Single(plan => plan.Plan == "starter");
        starter.ScoreMetrics.Should().Equal("retention_rate_pct", "customers_stopped_paying");
        starter.ChurnReasons.Single().Metrics.Should().Equal("never_started.left_pct", "never_started.stayed_pct");
    }

    [TestMethod]
    public void Validate_AReferenceToAnotherPlan_IsStillRejected()
    {
        // Only the plan's own key is dropped: Starter cannot cite Studio's figure this way.
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            ScoreMetrics = ["studio.retention_rate_pct"]
        });

        Errors(review).Should().Contain(error => error.Contains("cites studio.retention_rate_pct"));
    }

    [TestMethod]
    public void Build_SendsTheFinishedTrialsTheRateIsAShareOf()
    {
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m)) with
        {
            Trial = new TrialInsightDto(50, 11, 7, 5, 0, 27, 30.8m, 40m, [])
        };

        var trial = PlanAiReviewInputBuilder.Build(insights, [], [], new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 29)).Trial;

        trial.Converted.Should().Be(12);
        trial.Finished.Should().Be(39, "12 bought and 27 went back to Free; the 11 still running are left out");
        trial.StillRunning.Should().Be(11);
    }

    [TestMethod]
    public void Validate_AMissingPlan_RejectsTheReview()
    {
        var review = PlanAiReviewFixtures.ValidReview();
        review = review with { Plans = review.Plans.Where(plan => plan.Plan != "vip").ToList() };

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Equal("Plan vip is not reviewed.");
    }

    [TestMethod]
    public void Validate_ASummaryThatBreaksARule_IsWrittenByTheBackend()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with { Summary = "The retention_rate_pct fell." });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeTrue();
        result.Dropped.Should().ContainSingle().Which.Should().Contain("names the metric key retention_rate_pct");
        var starter = result.Review!.Plans.Single(plan => plan.Plan == "starter");
        starter.Summary.Should().Be("Starter has 113 subscribers and 27,517,313 VND in MRR.");
        starter.SummaryMetrics.Should().Equal("subscribers_now", "mrr_end_vnd");
    }

    [TestMethod]
    public void Validate_AStrengthWithAMadeUpFigure_IsLeftOutAndTheRestKept()
    {
        var review = Change(PlanAiReviewFixtures.ValidReview(), "starter", plan => plan with
        {
            Strengths = [new PlanAiFinding("48 new customers joined.", ["customers_new"]), .. plan.Strengths]
        });

        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());

        result.IsValid.Should().BeTrue();
        var starter = result.Review!.Plans.Single(plan => plan.Plan == "starter");
        starter.Strengths.Select(item => item.Text).Should().Equal("49 new customers joined.");
        starter.ChurnReasons.Should().HaveCount(1);
        result.Dropped.Should().ContainSingle().Which.Should().Be("Plan starter strength: \"48 new customers\" is not a figure in the input. Left out.");
    }

    [TestMethod]
    public void Validate_AnAnswerThatIsNotJson_IsTheOnlyKindRejectedWithoutAPlan()
    {
        PlanAiReviewValidator.Validate("{\"plans\": [", PlanAiReviewFixtures.Input()).Review.Should().BeNull();
    }

    [TestMethod]
    public void Build_RoundsEachMovementAndWorksOutTheEndAndTotalsFromThem()
    {
        // Annual prices make MRR fractional: start 100.4 + new 50.4 - churn 20.4 is 130.4, which rounds
        // to 130, while the rounded parts give 100 + 50 - 20 = 130 too; the second plan's halves differ.
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m), ("pro", "Pro", 749_750m));
        var movement = new List<MrrBridgeDto>
        {
            new() { PlanName = null, Start = 301.2m, New = 100.9m, Churn = 20.4m, End = 381.7m },
            new() { PlanName = "Starter", Start = 100.4m, New = 50.4m, Churn = 20.4m, End = 130.4m },
            new() { PlanName = "Pro", Start = 200.8m, New = 50.5m, End = 251.3m }
        };

        var input = PlanAiReviewInputBuilder.Build(insights, [], movement, new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29));

        var pro = input.Plans.Single(plan => plan.Key == "pro").Metrics;
        pro.MrrEndVnd.Should().Be(pro.MrrStartVnd + pro.MrrNewVnd - pro.MrrChurnVnd);
        input.Totals.MrrStartVnd.Should().Be(input.Plans.Sum(plan => plan.Metrics.MrrStartVnd));
        input.Totals.MrrEndVnd.Should().Be(input.Plans.Sum(plan => plan.Metrics.MrrEndVnd));
        input.Totals.MrrEndVnd.Should().Be(
            input.Totals.MrrStartVnd + input.Totals.MrrNewVnd + input.Totals.MrrPlanChangesVnd - input.Totals.MrrChurnVnd);
    }

    [TestMethod]
    public void Build_APlanWithNoRevenueRow_HasUnknownMrrAndCustomers()
    {
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m));

        var input = PlanAiReviewInputBuilder.Build(insights, [], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29));

        var metrics = input.Plans.Single().Metrics;
        metrics.MrrStartVnd.Should().BeNull();
        metrics.CustomersStart.Should().BeNull();
        input.Totals.MrrEndVnd.Should().BeNull();
    }

    [TestMethod]
    public void Build_ChurnFiguresFromASmallSample_NeverReachTheModel()
    {
        var insights = PlanAiReviewFixtures.Insights(("starter", "Starter", 249_750m)) with
        {
            ChurnSignals =
            [
                new PlanChurnSignalDto("starter", "Starter", 24, 7, 10, false, 14.3m, 42.9m, null,
                    [new FeatureChurnSignalDto("design_images", "Design images", true, 80m, 42.9m, null, null, null, 19, 3)],
                    ["Design images", "Listings"],
                    [new LeaveGroupDto("never_started", "Never got started", 3, 42.9m, 1, 4.2m, true)])
            ]
        };

        var plan = PlanAiReviewInputBuilder.Build(insights, [], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29)).Plans.Single();

        plan.ChurnProfile.EnoughData.Should().BeFalse();
        plan.ChurnProfile.LeaveGroups.Should().BeEmpty();
        plan.ChurnProfile.CoreFeatures.Should().Equal("Design images", "Listings");
        plan.Features.Single().UsedByChurnedPct.Should().BeNull();
    }

    [TestMethod]
    public void MinimumGroup_TheBackendThresholdForComparingWhoLeft_IsStillTen()
    {
        PlanUsageSignals.MinimumGroup.Should().Be(10);
    }

    // What was wrong with a review: why it was rejected, or what was left out of or rewritten in it.
    private static IReadOnlyList<string> Errors(PlanAiReviewOutput review)
    {
        var result = PlanAiReviewValidator.Validate(Json(review), PlanAiReviewFixtures.Input());
        var problems = result.Errors
            .Concat(result.Dropped.Select(item => item.Replace(" Left out.", "").Replace(" Written by the backend instead.", "")))
            .ToList();
        problems.Should().NotBeEmpty();
        return problems;
    }

    private static PlanAiReviewOutput Change(PlanAiReviewOutput review, string plan, Func<PlanAiAssessment, PlanAiAssessment> change) =>
        review with { Plans = review.Plans.Select(item => item.Plan == plan ? change(item) : item).ToList() };

    private static string Json(PlanAiReviewOutput review) => JsonSerializer.Serialize(review, PlanAiReviewInputBuilder.Json);
}

/// <summary>A small input and a review that follows it, for the AI review tests.</summary>
internal static class PlanAiReviewFixtures
{
    /// <summary>
    /// Starter has enough data and enough customers who left; Studio has enough data but too few
    /// who left; Vip has too little data to score. All three share Starter's plan figures.
    /// </summary>
    public static PlanAiReviewInput Input() => new(
        new PlanAiReviewPeriod(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 29), 90),
        "VND",
        new PlanAiReviewTotals(142_286_354m, 61_187_855m, 1_000_000m, 38_484_250m, 165_989_959m),
        [
            Plan("starter", "Starter", true, true, [
                new("design_images", "Design images", 79.6m, 76.6m, 48.6m, -8.1m, 4.6m, 2.7m, 90, 6m, 50, 3, 2.7m),
                new("uploaded_mockups", "Uploaded mockups", 22.1m, 25m, 17.1m, null, null, null)
            ]),
            Plan("studio", "Studio", true, false, [new("design_images", "Design images", 89.5m, null, null, null, null, null)]),
            Plan("vip", "Vip", false, false, [new("design_images", "Design images", 50m, null, null, null, null, null)])
        ],
        new PlanAiReviewTrialInput(70, 20, 28.6m, [
            new("ai_videos", "AI videos", 75m, 13.2m),
            new("exports", "ZIP exports", 0m, 0m)
        ]));

    public static PlanAiReviewOutput ValidReview() => new(
        [
            new PlanAiAssessment(
                "starter", "watch", 58, "Retention is 65.3% with 35 customers who stopped paying.",
                ["retention_rate_pct", "customers_stopped_paying"],
                "Starter grew to 113 subscribers.", ["customers_end"],
                new PlanAiOutlook("improving", "MRR rose 10.9% and the plan gained 12 customers on balance.", ["mrr_change_pct", "customers_net_change"]),
                [new PlanAiFinding("49 new customers joined.", ["customers_new"])],
                [],
                [
                    new PlanAiFeatureInsight("design_images", ["keeps_customers"], "76.6% of those who stayed used it, 48.6% of those who left.",
                        "Prompt new Sellers to make a design.", ["design_images.used_by_stayed_pct", "design_images.used_by_churned_pct"]),
                    new PlanAiFeatureInsight("uploaded_mockups", ["underused"], "Only 22.1% use it.", "Show it during setup.",
                        ["uploaded_mockups.adoption_pct"])
                ],
                [new PlanAiChurnReason("Leaving is linked to never using design images or rendered mockups.",
                    "51.4% of those who left never used design images or rendered mockups, against 4.7% of those who stayed.", "medium",
                    ["never_started.left_pct", "never_started.stayed_pct"], "This may mean they never got started.", "Check when those who left last signed in.")],
                null,
                [new PlanAiAction("Add a setup checklist for making a first design and mockup.", "high", "change", ["never_started.left_pct"])]),
            new PlanAiAssessment(
                "studio", "healthy", 80, "Retention is 65.3%.", ["retention_rate_pct"], "Studio grew to 113 subscribers.", ["customers_end"],
                new PlanAiOutlook("improving", "MRR rose 10.9%.", ["mrr_change_pct"]),
                [], [], [], [], null, []),
            new PlanAiAssessment(
                "vip", "not_enough_data", null, "Too few subscribers to score.", [], "Vip has too little data.", [],
                new PlanAiOutlook("stable", "The plan gained 12 customers on balance.", ["customers_net_change"]),
                [], [], [], [], null, [])
        ],
        "Trial conversion is 28.6%. AI videos show the largest gap: 75% against 13.2%.",
        ["trial.conversion_rate_pct", "trial.ai_videos.bought_pct", "trial.ai_videos.not_bought_pct"]);

    // Starter's 35 customers who left, one group each: 18 + 2 + 0 + 6 + 9.
    private static readonly IReadOnlyList<PlanAiReviewLeaveGroup> LeaveGroups =
    [
        new("never_started", 18, 51.4m, 4.7m, true),
        new("hit_failures", 2, 5.7m, 1.6m, false),
        new("hit_limits", 0, 0m, 3.1m, false),
        new("faded", 6, 17.1m, 9.4m, false),
        new("steady", 9, 25.7m, 81.2m, false),
    ];

    /// <summary>Plan Insights with the given paid plans, each with design images included and no churn comparison.</summary>
    public static PlanInsightsDto Insights(params (string Key, string Name, decimal Price)[] plans) => new(
        90,
        new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc),
        20,
        new TrialInsightDto(0, 0, 0, 0, 0, 0, null, null, []),
        plans.Select(plan => new PlanGroupInsightDto(plan.Key, plan.Name, false, 3, false, plan.Price,
            [new FeatureAdoptionDto("design_images", "Design images", true, 2, 66.7m)], new PlanCostDto(0m, plan.Price, 0m))).ToList(),
        [],
        [],
        [],
        new VerdictRulesDto(1.5m, 60m, 30m),
        []);

    private static PlanAiReviewPlanInput Plan(string key, string name, bool enoughData, bool churnEnough, IReadOnlyList<PlanAiReviewFeatureInput> features) =>
        new(key, name, enoughData, null, null,
            new PlanAiReviewPlanMetrics(249_750m, 113, 24_810_375m, 11_947_688m, 0m, 0m, 0m, 499_500m, 8_741_250m, 27_517_313m,
                101, 64, 2, 35, 49, 0, 113, 65.3m, 0, null, null, null) with { MrrChangePct = 10.9m, CustomersNetChange = 12 },
            new PlanAiReviewChurnProfile(churnEnough, churnEnough ? 2.9m : null, churnEnough ? 26.2m : null, ["Design images", "Rendered mockups"],
                churnEnough ? LeaveGroups : []),
            features.Select(feature => feature with { Flags = PlanAiReviewRules.FeatureFlags(feature, 30m) }).ToList(),
            PlanAiReviewRules.Outlook(24_810_375m, 27_517_313m, 12));
}
