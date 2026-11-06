using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanAiReviewRulesTests
{
    [TestMethod]
    [DataRow(100, 104, 0, "stable", DisplayName = "MRR +4%")]
    [DataRow(100, 105, 0, "improving", DisplayName = "MRR +5%")]
    [DataRow(100, 95, 3, "declining", DisplayName = "MRR -5% with more customers")]
    [DataRow(100, 120, -1, "declining", DisplayName = "MRR up but one customer lost on balance")]
    [DataRow(0, 50, 2, "improving", DisplayName = "First revenue")]
    [DataRow(0, 0, 0, "stable", DisplayName = "No revenue")]
    public void Outlook_FollowsMrrChangeAndNetCustomers(int start, int end, int netChange, string expected) =>
        PlanAiReviewRules.Outlook(start, end, netChange).Should().Be(expected);

    [TestMethod]
    public void MrrChangePct_IsRoundedToOneDecimalAndUnknownWithoutAStart()
    {
        // (27,517,313 - 24,810,375) / 24,810,375 = 10.91%
        PlanAiReviewRules.MrrChangePct(24_810_375m, 27_517_313m).Should().Be(10.9m);
        PlanAiReviewRules.MrrChangePct(0m, 1_000m).Should().BeNull();
        PlanAiReviewRules.CustomersNetChange(101, 113).Should().Be(12);
    }

    [TestMethod]
    [DataRow(true, 75, "healthy")]
    [DataRow(true, 74, "watch")]
    [DataRow(true, 50, "watch")]
    [DataRow(true, 49, "at_risk")]
    [DataRow(false, 90, "not_enough_data")]
    public void Status_IsTheScoresBand(bool enoughData, int score, string expected) =>
        PlanAiReviewRules.Status(enoughData, score).Should().Be(expected);

    [TestMethod]
    public void FeatureFlags_MarkWhatIsNotable()
    {
        static PlanAiReviewFeatureInput Feature(decimal adoption, decimal? stayed = null, decimal? left = null,
            decimal? usageChange = null, decimal? stayedFailures = null, decimal? churnedFailures = null) =>
            new("design_images", "Design images", adoption, stayed, left, usageChange, stayedFailures, churnedFailures);

        PlanAiReviewRules.FeatureFlags(Feature(22.1m), 30m).Should().Equal("underused");
        PlanAiReviewRules.FeatureFlags(Feature(30m), 30m).Should().BeEmpty();
        PlanAiReviewRules.FeatureFlags(Feature(79.6m, 76.6m, 48.6m), 30m).Should().Equal("keeps_customers");
        PlanAiReviewRules.FeatureFlags(Feature(79.6m, 56m, 48m), 30m).Should().BeEmpty("8 points is too small a gap");
        PlanAiReviewRules.FeatureFlags(Feature(79.6m, stayedFailures: 2m, churnedFailures: 3m), 30m).Should().Equal("needs_improvement");
        PlanAiReviewRules.FeatureFlags(Feature(79.6m, stayedFailures: 12m, churnedFailures: 3m), 30m).Should().Equal("needs_improvement");
        PlanAiReviewRules.FeatureFlags(Feature(79.6m, usageChange: -25m), 30m).Should().Equal("needs_improvement");
    }
}
