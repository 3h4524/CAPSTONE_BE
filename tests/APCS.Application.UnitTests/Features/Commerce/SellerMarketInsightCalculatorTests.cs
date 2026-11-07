using System.Text.Json;
using APCS.Application.Features.Commerce;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class SellerMarketInsightCalculatorTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 12, 0, 0);

    [TestMethod]
    public void Calculate_WhenOnlyTheSampleGrows_DoesNotReportGrowingSupply()
    {
        // A second run on day 2 adds listings to the sample; the market itself did not change.
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["a", "b", "c", "d", "e"], views: 100));
        rows.AddRange(Listings(Day1.AddDays(1), ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"], views: 100));

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, []);

        result.ListingTrend.Should().Be("unknown");
        result.ListingGrowthPercent.Should().BeNull();
        result.CompetitionLevel.Should().Be("unknown");
    }

    [TestMethod]
    public void Calculate_UsesMarketplaceTotalsForSupplyTrendAndCompetition()
    {
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100);
        var supply = MarketSupplyCalculator.Calculate(
        [
            Observation(new DateOnly(2026, 1, 1), 40_000),
            Observation(new DateOnly(2026, 1, 8), 48_000)
        ]) with { SizePercentile = 50 };

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, [], supply);

        result.ListingTrend.Should().Be("growing");
        result.ListingGrowthPercent.Should().Be(20);
        result.ActiveListingCount.Should().Be(48_000);
        result.CompetitionLevel.Should().Be("medium");
    }

    [TestMethod]
    public void Calculate_WhenDifferentListingsHaveDifferentRates_DoesNotReportEngagementChange()
    {
        // Day 1 and day 2 sample completely different listings, so no listing can be compared.
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["a", "b", "c", "d", "e"], views: 100));
        rows.AddRange(Listings(Day1.AddDays(1), ["f", "g", "h", "i", "j"], views: 900));
        rows.AddRange(Listings(Day1.AddDays(2), ["k", "l", "m", "n", "o"], views: 5));

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, []);

        result.EngagementTrend.Should().Be("unknown");
        result.EngagementChangePercent.Should().BeNull();
    }

    [TestMethod]
    public void CalculateEngagementTrend_ComparesViewVelocityOfTheSameListings()
    {
        // Each listing gains 10 views a day early on and 20 views a day later.
        var ids = new[] { "a", "b", "c" };
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ids, views: 100));
        rows.AddRange(Listings(Day1.AddDays(1), ids, views: 110));
        rows.AddRange(Listings(Day1.AddDays(2), ids, views: 130));

        var (trend, change) = SellerMarketInsightCalculator.CalculateEngagementTrend(rows);

        trend.Should().Be("up");
        change.Should().Be(100);
    }

    [TestMethod]
    public void CalculateEngagementTrend_WhenTooFewListingsArePaired_ReturnsUnknown()
    {
        var ids = new[] { "a", "b" };
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ids, views: 100));
        rows.AddRange(Listings(Day1.AddDays(1), ids, views: 110));
        rows.AddRange(Listings(Day1.AddDays(2), ids, views: 130));

        SellerMarketInsightCalculator.CalculateEngagementTrend(rows).Trend.Should().Be("unknown");
    }

    [TestMethod]
    public void Calculate_ReadsDemandFromFavoritesPerListingAge()
    {
        // Five listings observed 60 days after creation with 4 favorites: 2 favorites per 30 days.
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100, favorites: 4, createdAt: Day1.AddDays(-60));

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, []);

        result.DemandProxy.Should().Be("high");
        result.Signals.MedianFavoritesPer30Days.Should().Be(2);
        result.EvidenceQuality.Should().Be("low");
    }

    [TestMethod]
    public void Calculate_WhenOldListingsHaveManyFavorites_DoesNotReportHighDemand()
    {
        // 50 favorites collected over 10 years is about 0.4 favorites per 30 days.
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100, favorites: 50, createdAt: Day1.AddDays(-3650));

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, []);

        result.DemandProxy.Should().Be("medium");
    }

    [TestMethod]
    public void Calculate_WhenFewerThanFiveListings_MarksEvidenceInsufficient()
    {
        var rows = Listings(Day1, ["a", "b", "c"], views: 100);

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, []);

        result.DemandProxy.Should().Be("insufficient");
        result.EvidenceQuality.Should().Be("insufficient");
        result.InsightType.Should().Be("insufficient_evidence");
        result.Recommendation.Should().Contain("Collect more observations");
    }

    [TestMethod]
    public void Calculate_WhenDemandIsHighAndCompetitionIsLow_ReturnsPromisingSignal()
    {
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100, favorites: 4, createdAt: Day1.AddDays(-60));
        var supply = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 1), 2_000)]) with { SizePercentile = 10 };

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, [], supply);

        result.CompetitionLevel.Should().Be("low");
        result.InsightType.Should().Be("promising_signal");
        result.ActionPlan.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Calculate_WhenCompetitionIsHighAndDemandLow_ReturnsCrowdedMarket()
    {
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100, favorites: 1, createdAt: Day1.AddDays(-3650));
        var supply = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 1), 250_000)]) with { SizePercentile = 90 };

        var result = SellerMarketInsightCalculator.Calculate("gift", rows, [], supply);

        result.DemandProxy.Should().Be("low");
        result.InsightType.Should().Be("crowded_market");
    }

    internal static MarketQueryObservation Observation(DateOnly day, int? total) => new()
    {
        Id = Guid.NewGuid(),
        Platform = "etsy",
        Query = "gift",
        ObservedOn = day,
        TotalResults = total,
        SampleSize = 100,
        LimitUsed = 100,
        SortOn = "score",
        ObservedAt = day.ToDateTime(new TimeOnly(12, 0))
    };

    internal static IReadOnlyList<MarketListingSnapshot> Listings(
        DateTime observedAt,
        IReadOnlyList<string> ids,
        int views,
        int favorites = 1,
        DateTime? createdAt = null,
        bool? personalizable = null,
        decimal? price = null,
        Func<int, string>? seller = null,
        string query = "gift") =>
        ids.Select((id, index) => new MarketListingSnapshot
        {
            Id = Guid.NewGuid(),
            Platform = "etsy",
            ExternalListingId = id,
            ExternalSellerId = seller?.Invoke(index) ?? $"seller-{id}",
            Title = "Listing " + id,
            Favorites = favorites,
            Views = views,
            Price = price,
            Tags = "[]",
            MatchedQuery = query,
            SourceUrl = $"https://etsy.com/listing/{id}",
            ObservedAt = observedAt,
            CollectedAt = observedAt.AddMinutes(1),
            RawPayload = Payload(createdAt, personalizable)
        }).ToArray();

    private static string? Payload(DateTime? createdAt, bool? personalizable)
    {
        if (createdAt is null && personalizable is null)
            return null;

        var payload = new Dictionary<string, object?>();
        if (createdAt is not null)
            payload["original_creation_timestamp"] = new DateTimeOffset(DateTime.SpecifyKind(createdAt.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (personalizable is not null)
            payload["is_personalizable"] = personalizable.Value;
        return JsonSerializer.Serialize(payload);
    }
}
