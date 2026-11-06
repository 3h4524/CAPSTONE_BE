using APCS.Application.Features.Commerce;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketOpportunityInsightCalculatorTests
{
    [TestMethod]
    public void Calculate_UsesLatestListingSnapshotsAndBuildsActionableInsights()
    {
        var opportunity = new MarketOpportunity
        {
            Confidence = 0.85m,
            ScoreBreakdown = "{\"viralScore\":82,\"favoritesScore\":76,\"viewsScore\":88,\"formulaVersion\":\"viral-v1\"}",
            FirstObservedAt = new DateTime(2026, 1, 1),
            LastObservedAt = new DateTime(2026, 1, 10)
        };
        var evidence = new[]
        {
            new MarketOpportunityEvidenceInput("listing-1", "seller-1", "First listing", "https://example.com/1", 20, "USD", 10, 100, new DateTime(2026, 1, 1)),
            new MarketOpportunityEvidenceInput("listing-1", "seller-1", "First listing", "https://example.com/1", 22, "USD", 20, 100, new DateTime(2026, 1, 10)),
            new MarketOpportunityEvidenceInput("listing-2", "seller-2", "Second listing", null, 30, "USD", 5, 100, new DateTime(2026, 1, 10))
        };

        var result = MarketOpportunityInsightCalculator.Calculate(opportunity, evidence, new DateTime(2026, 1, 10));

        result.EvidenceCount.Should().Be(3);
        result.DistinctListingCount.Should().Be(2);
        result.DistinctSellerCount.Should().Be(2);
        result.MedianPrice.Should().Be(26);
        result.EngagementRate.Should().Be(12.5);
        result.CompetitionLevel.Should().Be("unknown");
        result.ConfidenceLabel.Should().Be("low");
        result.EvidenceQuality.Should().Be("insufficient");
        result.PriceLabel.Should().Be("observed");
        result.ObservationPeriodCount.Should().Be(2);
        result.MarketScoreBreakdown.Should().ContainKey("favoritesScore");
        result.MarketScoreBreakdown.Should().NotContainKey("formulaVersion");
        result.Evidence.Should().HaveCount(2);
        result.RealEvidenceCount.Should().Be(3);
        result.SyntheticEvidenceCount.Should().Be(0);
        result.FreshnessLabel.Should().Be("fresh");
        result.FreshnessAgeDays.Should().Be(0);
    }

    [TestMethod]
    public void Calculate_WhenThereIsNotEnoughHistory_MarksTrendUnknown()
    {
        var opportunity = new MarketOpportunity { Confidence = 0.2m, ScoreBreakdown = "{}" };
        var evidence = new[]
        {
            new MarketOpportunityEvidenceInput("listing-1", null, "Listing", null, null, null, null, null, new DateTime(2026, 1, 1))
        };

        var result = MarketOpportunityInsightCalculator.Calculate(opportunity, evidence);

        result.EngagementTrend.Should().Be("unknown");
        result.EngagementRate.Should().BeNull();
        result.ConfidenceLabel.Should().Be("low");
    }

    [TestMethod]
    public void Calculate_WhenSnapshotIsOld_MarksItStale()
    {
        var opportunity = new MarketOpportunity { Confidence = 0.8m, ScoreBreakdown = "{}" };
        var evidence = new[]
        {
            new MarketOpportunityEvidenceInput("listing-1", null, "Listing", null, null, null, null, null, new DateTime(2026, 1, 1), true)
        };

        var result = MarketOpportunityInsightCalculator.Calculate(opportunity, evidence, new DateTime(2026, 2, 15));

        result.FreshnessLabel.Should().Be("stale");
        result.FreshnessAgeDays.Should().Be(45);
        result.DataSource.Should().Be("synthetic");
        result.SyntheticEvidenceCount.Should().Be(1);
    }
}
