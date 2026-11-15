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

    private static MarketOpportunityEvidenceInput Seen(string listing, int favorites, int day, int hour = 12) =>
        new(listing, "seller", "Listing", null, 20, "USD", favorites, 1000, new DateTime(2026, 1, day, hour, 0, 0));

    [TestMethod]
    public void Calculate_AListingGainingFavoritesFaster_IsRising_AndCountsItsDailyGain()
    {
        var opportunity = new MarketOpportunity { Confidence = 0.6m, ScoreBreakdown = "{}" };
        // 2 then 2 favorites a day, then 6 and 6: the pace tripled.
        var evidence = new[] { Seen("a", 10, 1), Seen("a", 12, 2), Seen("a", 14, 3), Seen("a", 20, 4), Seen("a", 26, 5) };

        var result = MarketOpportunityInsightCalculator.Calculate(opportunity, evidence, new DateTime(2026, 1, 5));

        result.FavoritesPerDay.Should().Be(4);
        result.EngagementChangePercent.Should().Be(200);
        result.EngagementTrend.Should().Be("up");
    }

    [TestMethod]
    public void DemandVelocity_ANewListingJoiningTheEvidence_IsNotGrowth_AndADaysLastFiguresCount()
    {
        // Listing b appears late with many favorites; listing a is steady at 3 a day, read twice on day 2.
        var evidence = new[]
        {
            Seen("a", 10, 1), Seen("a", 11, 2, hour: 8), Seen("a", 13, 2, hour: 20), Seen("a", 16, 3), Seen("a", 19, 4),
            Seen("b", 500, 4),
        };

        var (perDay, change) = MarketOpportunityInsightCalculator.DemandVelocity(evidence);

        perDay.Should().Be(3);
        change.Should().Be(0);
    }

    [TestMethod]
    public void DemandVelocity_ListingsSeenOnOneDayOnly_GiveNoReading()
    {
        var (perDay, change) = MarketOpportunityInsightCalculator.DemandVelocity([Seen("a", 10, 1), Seen("b", 40, 1)]);

        perDay.Should().BeNull();
        change.Should().BeNull();
    }
}
