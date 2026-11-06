using System.Text.Json;
using APCS.Domain.Entities;
using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketQueryMetricsCalculatorTests
{
    [TestMethod]
    public void Calculate_UsesLatestListingValuesAndAggregatesMarketMetrics()
    {
        var rows = new[]
        {
            Snapshot("listing-1", "seller-1", 20, 10, new DateTime(2026, 1, 1), ["gift", "custom"]),
            Snapshot("listing-1", "seller-1", 22, 20, new DateTime(2026, 1, 8), ["gift", "custom"]),
            Snapshot("listing-2", "seller-2", 30, 5, new DateTime(2026, 1, 8), ["gift", "personalized"])
        };

        var result = MarketQueryMetricsCalculator.Calculate("wedding gift", rows);

        result.DistinctListingCount.Should().Be(2);
        result.DistinctSellerCount.Should().Be(2);
        result.MedianPrice.Should().Be(26);
        result.AverageFavorites.Should().Be(12.5);
        result.ObservationPeriodCount.Should().Be(2);
        result.NewListingCount.Should().Be(1);
        result.TopTags.Should().Contain(new MarketTagMetric("gift", 2));
        result.Confidence.Should().Be("low");
    }

    [TestMethod]
    public void Calculate_RejectsInvalidRowsAndDeduplicatesSameListingPeriod()
    {
        var valid = Snapshot("listing-1", "seller-1", 20, 10, new DateTime(2026, 1, 1), []);
        var duplicate = Snapshot("listing-1", "seller-1", 21, 11, new DateTime(2026, 1, 1), []);
        var invalid = Snapshot("", "seller-2", -1, -2, new DateTime(2026, 1, 1), []);

        var result = MarketQueryMetricsCalculator.Calculate("gift", [valid, duplicate, invalid]);

        result.RawSnapshotCount.Should().Be(3);
        result.EligibleSnapshotCount.Should().Be(1);
        result.RejectedSnapshotCount.Should().Be(2);
        result.RejectionReasons.Should().ContainKey("duplicate_snapshot").WhoseValue.Should().Be(1);
        result.RejectionReasons.Should().ContainKey("missing_listing_id");
    }

    private static MarketListingSnapshot Snapshot(
        string listingId,
        string sellerId,
        decimal price,
        int favorites,
        DateTime observedAt,
        IReadOnlyCollection<string> tags) =>
        new()
        {
            Id = Guid.NewGuid(),
            Platform = "etsy",
            ExternalListingId = listingId,
            ExternalSellerId = sellerId,
            Title = "Listing",
            Price = price,
            Favorites = favorites,
            Tags = JsonSerializer.Serialize(tags),
            MatchedQuery = "gift",
            ObservedAt = observedAt,
            CollectedAt = observedAt.AddMinutes(1)
        };
}
