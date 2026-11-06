using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class SellerOpportunityScorerTests
{
    [TestMethod]
    public void Score_WhenOpportunityFitsSeller_ReturnsHighFitAndExplainableBreakdown()
    {
        var opportunity = new MarketOpportunityCandidate(80, 0.8, "home decor", ["personalized", "wood"], 45);
        var profile = new SellerResearchProfileSnapshot(
            ["home decor"], ["personalized", "wood"], ["home decor"], [], 30, 60);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.SellerFitScore.Should().Be(100);
        result.FinalScore.Should().Be(88);
        result.Breakdown.Should().ContainKey("keywordFit").WhoseValue.Should().Be(100);
    }

    [TestMethod]
    public void Score_WhenCategoryIsExcluded_PenalizesTheOpportunity()
    {
        var opportunity = new MarketOpportunityCandidate(90, 0.7, "clothing", ["shirt"], 25);
        var profile = new SellerResearchProfileSnapshot([], ["shirt"], [], ["clothing"], 10, 50);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.SellerFitScore.Should().Be(0);
        result.FinalScore.Should().Be(54);
        result.Explanation.Should().Contain("excluded category");
    }

    [TestMethod]
    public void Score_WhenPriceIsOutsideSellerRange_ReducesPriceFitOnly()
    {
        var opportunity = new MarketOpportunityCandidate(70, 0.6, "gifts", ["custom"], 120);
        var profile = new SellerResearchProfileSnapshot([], ["custom"], ["gifts"], [], 20, 80);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.Breakdown["categoryFit"].Should().Be(100);
        result.Breakdown["keywordFit"].Should().Be(100);
        result.Breakdown["priceFit"].Should().Be(0);
        result.SellerFitScore.Should().Be(75);
    }

    [TestMethod]
    public void Score_WhenEvidenceIsInsufficient_DowngradesMarketScoreAndExplainsWhy()
    {
        var opportunity = new MarketOpportunityCandidate(
            100,
            0.5,
            "gifts",
            ["custom"],
            45,
            EvidenceCount: 1,
            DistinctListingCount: 1,
            DistinctSellerCount: 1,
            ObservationPeriodCount: 1);
        var profile = new SellerResearchProfileSnapshot([], ["custom"], ["gifts"], [], 30, 60);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.Breakdown["evidenceQualityScore"].Should().Be(35);
        result.Breakdown["evidenceAdjustedMarketScore"].Should().Be(65);
        result.FinalScore.Should().Be(79);
        result.Explanation.Should().Contain("insufficient");
    }

    [TestMethod]
    public void IsQualified_WhenSellerFitAndFinalScoresMeetThresholds_ReturnsTrue()
    {
        var opportunity = new MarketOpportunityCandidate(70, 0.8, "gifts", ["custom"], 45);
        var profile = new SellerResearchProfileSnapshot([], ["custom"], ["gifts"], [], 30, 60);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        SellerOpportunityThresholds.IsQualified(result).Should().BeTrue();
    }

    [TestMethod]
    public void IsQualified_WhenSellerFitIsBelowThreshold_ReturnsFalse()
    {
        var opportunity = new MarketOpportunityCandidate(95, 0.8, "other", ["unrelated"], 45);
        var profile = new SellerResearchProfileSnapshot([], ["custom"], ["gifts"], [], 30, 60);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.SellerFitScore.Should().BeLessThan(SellerOpportunityThresholds.MinimumSellerFitScore);
        SellerOpportunityThresholds.IsQualified(result).Should().BeFalse();
    }
}
