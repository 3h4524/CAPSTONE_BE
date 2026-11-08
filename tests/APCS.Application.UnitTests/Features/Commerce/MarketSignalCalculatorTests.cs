using APCS.Application.Features.Commerce;
using APCS.Domain.Entities;
using FluentAssertions;
using static APCS.Application.UnitTests.Features.Commerce.SellerMarketInsightCalculatorTests;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketSignalCalculatorTests
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 12, 0, 0);

    [TestMethod]
    public void Supply_WithOneObservedDay_KeepsTotalButNoTrend()
    {
        var result = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 1), 12_000)]);

        result.ActiveListingCount.Should().Be(12_000);
        result.Trend.Should().Be("unknown");
        result.ChangePercent.Should().BeNull();
        result.ObservationDayCount.Should().Be(1);
    }

    [TestMethod]
    public void Supply_ComparesEarlierAndLaterHalfOfObservedDays()
    {
        var result = MarketSupplyCalculator.Calculate(
        [
            Observation(new DateOnly(2026, 1, 1), 10_000),
            Observation(new DateOnly(2026, 1, 2), 10_000),
            Observation(new DateOnly(2026, 1, 3), 9_000),
            Observation(new DateOnly(2026, 1, 4), 9_000)
        ]);

        result.Trend.Should().Be("reducing");
        result.ChangePercent.Should().Be(-10);
        result.ActiveListingCount.Should().Be(9_000);
        result.LatestObservedOn.Should().Be(new DateOnly(2026, 1, 4));
    }

    [TestMethod]
    public void Supply_WithinThreshold_IsStable()
    {
        var result = MarketSupplyCalculator.Calculate(
        [
            Observation(new DateOnly(2026, 1, 1), 10_000),
            Observation(new DateOnly(2026, 1, 2), 10_300)
        ]);

        result.Trend.Should().Be("stable");
    }

    [TestMethod]
    public void Supply_IgnoresObservationsWithoutTotal()
    {
        var result = MarketSupplyCalculator.Calculate(
        [
            Observation(new DateOnly(2026, 1, 1), null),
            Observation(new DateOnly(2026, 1, 2), 8_000)
        ]);

        result.ActiveListingCount.Should().Be(8_000);
        result.Trend.Should().Be("unknown");
        result.ObservationDayCount.Should().Be(1);
    }

    [TestMethod]
    public void Supply_Combine_UsesMedianKeywordAndAverageChange()
    {
        var result = MarketSupplyCalculator.Combine(
        [
            new MarketSupplySignal(1_000, "growing", 10, 3, new DateOnly(2026, 1, 3), 10),
            new MarketSupplySignal(20_000, "stable", 2, 3, new DateOnly(2026, 1, 3), 50),
            new MarketSupplySignal(400_000, "growing", 12, 2, new DateOnly(2026, 1, 2), 90),
            MarketSupplySignal.Unknown
        ]);

        result.ActiveListingCount.Should().Be(20_000);
        result.SizePercentile.Should().Be(50);
        result.ChangePercent.Should().Be(8);
        result.Trend.Should().Be("growing");
        result.ObservationDayCount.Should().Be(3);
    }

    [TestMethod]
    [DataRow(null, "unknown")]
    [DataRow(10d, "low")]
    [DataRow(33.3d, "low")]
    [DataRow(50d, "medium")]
    [DataRow(66.6d, "medium")]
    [DataRow(66.7d, "high")]
    [DataRow(100d, "high")]
    public void Supply_Competition_UsesSizePercentileThirds(double? percentile, string expected) =>
        MarketSupplyCalculator.Competition(percentile).Should().Be(expected);

    [TestMethod]
    public void Supply_RankSizes_SetsPercentileAmongTrackedQueries()
    {
        var supply = new Dictionary<string, MarketSupplySignal>
        {
            ["a"] = Known(158_157),
            ["b"] = Known(211_749),
            ["c"] = Known(412_772),
            ["d"] = Known(1_919_158),
            ["e"] = Known(3_867_069),
            ["unknown"] = MarketSupplySignal.Unknown
        };

        var ranked = MarketSupplyCalculator.RankSizes(supply);

        ranked["a"].SizePercentile.Should().Be(20);
        ranked["c"].SizePercentile.Should().Be(60);
        ranked["e"].SizePercentile.Should().Be(100);
        ranked["unknown"].SizePercentile.Should().BeNull();
        MarketSupplyCalculator.Competition(ranked["a"].SizePercentile).Should().Be("low");
        MarketSupplyCalculator.Competition(ranked["e"].SizePercentile).Should().Be("high");
    }

    [TestMethod]
    public void Supply_RankSizes_WithTooFewQueries_LeavesPercentileEmpty()
    {
        var supply = new Dictionary<string, MarketSupplySignal>
        {
            ["a"] = Known(1_000),
            ["b"] = Known(2_000)
        };

        MarketSupplyCalculator.RankSizes(supply).Values.Should().OnlyContain(item => item.SizePercentile == null);
    }

    [TestMethod]
    public void Signals_UseLatestObservationOfEachListing()
    {
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["a"], views: 10, favorites: 1, createdAt: Day1.AddDays(-30)));
        rows.AddRange(Listings(Day1.AddDays(30), ["a"], views: 50, favorites: 6, createdAt: Day1.AddDays(-30)));

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.SampleListingCount.Should().Be(1);
        result.MedianListingAgeDays.Should().Be(60);
        result.MedianFavoritesPer30Days.Should().Be(3);
    }

    [TestMethod]
    public void Signals_CountNewAndRisingListingsAndPersonalization()
    {
        var rows = new List<MarketListingSnapshot>();
        // Two new listings (10 days old), one of them already rising.
        rows.AddRange(Listings(Day1, ["new-rising"], views: 100, favorites: 2, createdAt: Day1.AddDays(-10), personalizable: true));
        rows.AddRange(Listings(Day1, ["new-quiet"], views: 100, favorites: 0, createdAt: Day1.AddDays(-10), personalizable: true));
        // A 60-day-old listing with 6 favorites (3 per 30 days) is rising but not new.
        rows.AddRange(Listings(Day1, ["young-rising"], views: 100, favorites: 6, createdAt: Day1.AddDays(-60), personalizable: false));
        // Two old listings with many lifetime favorites are neither new nor rising.
        rows.AddRange(Listings(Day1, ["old-1", "old-2"], views: 100, favorites: 400, createdAt: Day1.AddDays(-1000), personalizable: false));

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.NewListingSharePercent.Should().Be(40);
        result.RisingNewListingCount.Should().Be(2);
        result.PersonalizedSharePercent.Should().Be(40);
    }

    [TestMethod]
    public void Signals_ComputePriceQuartilesAndTopSellerShare()
    {
        var ids = Enumerable.Range(1, 10).Select(index => $"l{index}").ToArray();
        var rows = ids.SelectMany((id, index) => Listings(
                Day1,
                [id],
                views: 100,
                price: 10 + index,
                // Seller s0 owns five listings; the other five belong to five different sellers.
                seller: _ => index < 5 ? "s0" : $"s{index}"))
            .ToArray();

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.PriceP25.Should().Be(12.25m);
        result.MedianPrice.Should().Be(14.5m);
        result.PriceP75.Should().Be(16.75m);
        // Top five sellers: s0 (5 listings) plus four single-listing sellers = 9 of 10.
        result.TopSellerSharePercent.Should().Be(90);
    }

    [TestMethod]
    public void Signals_WithFewListingsOrMissingPayload_LeaveSharesEmpty()
    {
        var rows = Listings(Day1, ["a", "b"], views: 100);

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.SampleListingCount.Should().Be(2);
        result.MedianListingAgeDays.Should().BeNull();
        result.MedianFavoritesPer30Days.Should().BeNull();
        result.NewListingSharePercent.Should().BeNull();
        result.PersonalizedSharePercent.Should().BeNull();
        result.TopSellerSharePercent.Should().BeNull();
        result.RisingNewListingCount.Should().Be(0);
    }

    [TestMethod]
    public void Signals_IgnoreMalformedPayload()
    {
        var rows = Listings(Day1, ["a", "b", "c", "d", "e"], views: 100)
            .Select(item => { item.RawPayload = "{not json"; return item; })
            .ToArray();

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.SampleListingCount.Should().Be(5);
        result.MedianListingAgeDays.Should().BeNull();
    }

    [TestMethod]
    public void Niche_UsesCombinedSupplyAndDropsSampleBasedSellerTrend()
    {
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["a", "b", "c", "d", "e"], views: 100, favorites: 4, createdAt: Day1.AddDays(-60)));
        rows.AddRange(Listings(Day1.AddDays(1), ["a", "b", "c", "d", "e", "f", "g"], views: 100, favorites: 4, createdAt: Day1.AddDays(-60)));
        var supply = new MarketSupplySignal(80_000, "growing", 12, 2, new DateOnly(2026, 1, 2), 90);

        var result = SellerNicheInsightCalculator.Calculate("gifts", ["gift"], rows, 1, [], supply);

        result.CompetitionLevel.Should().Be("high");
        result.CompetitorTrend.Should().Be("growing");
        result.ListingTrend.Should().Be("growing");
        result.MarketSupply.ActiveListingCount.Should().Be(80_000);
        result.Verdict.Should().Be("watch_and_differentiate");
        result.Conclusion.Should().Contain("80,000 active listings");
        result.Competitors.Should().OnlyContain(item => item.ListingTrend == "unknown" && item.ListingGrowthPercent == null);
        result.Highlights.Should().Contain(item => item.Contains("90% of the tracked niches"));
        result.ActionPlan.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Signals_UseOnlyTheLatestCollectionOfEachQuery()
    {
        // Listings that dropped out of the top on day 2 must not stay in the sample.
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["a", "b", "c", "old-1", "old-2"], views: 100));
        rows.AddRange(Listings(Day1.AddDays(1), ["a", "b", "c"], views: 100));
        // Another query collected only on day 1 keeps its own latest collection.
        rows.AddRange(Listings(Day1, ["x", "y"], views: 100, query: "hoodie"));

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.SampleListingCount.Should().Be(5);
        result.LatestCollectedOn.Should().Be(new DateOnly(2026, 1, 2));
    }

    [TestMethod]
    public void Signals_DoNotExtrapolateAVeryYoungListingIntoTraction()
    {
        // One favorite on day one would read as 30 a month; with the 14-day floor it is about 2.
        var rows = Listings(Day1, ["day-old"], views: 100, favorites: 1, createdAt: Day1.AddDays(-1), seller: _ => "s1").ToList();

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.MedianFavoritesPer30Days.Should().BeApproximately(2.14, 0.01);
        result.StrongSellerCount.Should().Be(0);
        result.RisingListings.Should().BeEmpty();
    }

    [TestMethod]
    public void Signals_CountStrongSellersAndListRisingListingsFastestFirst()
    {
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["slow-new"], views: 100, favorites: 3, createdAt: Day1.AddDays(-30), seller: _ => "s1"));
        rows.AddRange(Listings(Day1, ["fast-new"], views: 100, favorites: 6, createdAt: Day1.AddDays(-10), seller: _ => "s2"));
        rows.AddRange(Listings(Day1, ["old-strong"], views: 100, favorites: 400, createdAt: Day1.AddDays(-300), seller: _ => "s3"));
        rows.AddRange(Listings(Day1, ["quiet-1", "quiet-2"], views: 100, favorites: 0, createdAt: Day1.AddDays(-300), seller: _ => "s4"));

        var result = MarketListingSignalsCalculator.Calculate(rows);

        result.SellerCount.Should().Be(4);
        result.StrongSellerCount.Should().Be(3);
        result.RisingListings.Select(item => item.Title).Should().Equal("Listing fast-new", "Listing slow-new");
        // 6 favorites in 10 days: the rate divides by the 14-day minimum, not by 10.
        result.RisingListings[0].FavoritesPer30Days.Should().Be(12.9);
        result.RisingListings[0].AgeDays.Should().Be(10);
        result.RisingListings[0].Url.Should().Be("https://etsy.com/listing/fast-new");
    }

    [TestMethod]
    public void Highlights_DescribeOnlySignalsWithData()
    {
        var quiet = MarketListingSignalsCalculator.Calculate(Listings(Day1, ["a", "b"], views: 100));

        SellerNicheInsightCalculator.Highlights(MarketSupplySignal.Unknown, quiet).Should().BeEmpty();
    }

    [TestMethod]
    public void Highlights_WhenFewStrongSellersAndNewListingsRise_SaysTheTopIsOpen()
    {
        var ids = Enumerable.Range(1, 10).Select(index => $"l{index}").ToArray();
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ids[..9], views: 100, favorites: 1, createdAt: Day1.AddDays(-700), price: 20));
        rows.AddRange(Listings(Day1, ids[9..], views: 100, favorites: 9, createdAt: Day1.AddDays(-15), price: 30));
        var signals = MarketListingSignalsCalculator.Calculate(rows);

        var highlights = SellerNicheInsightCalculator.Highlights(MarketSupplySignal.Unknown, signals);

        highlights.Should().Contain(item => item.StartsWith("Only 1 of the 10 sellers"));
        highlights.Should().Contain(item => item.StartsWith("1 listing(s) under 90 days old"));
        highlights.Should().Contain(item => item.Contains("between 20.00 and 20.00"));
    }

    [TestMethod]
    public void Signals_SplitTopListingsIntoShareTiers()
    {
        // Seller s0 owns 6 listings, s1..s19 one each, s20..s23 one each: 29 listings in total.
        var rows = Enumerable.Range(0, 29)
            .SelectMany(index => Listings(Day1, [$"l{index}"], views: 100, seller: _ => index < 6 ? "s0" : $"s{index - 5}"))
            .ToArray();

        var tiers = MarketListingSignalsCalculator.Calculate(rows).ShareTiers;

        // Top 5 sellers: s0 (6) + four singles = 10 of 29; next 15 sellers: 15 of 29; rest: 4 of 29.
        tiers.Should().NotBeNull();
        tiers!.TopFivePercent.Should().Be(34.5);
        tiers.NextFifteenPercent.Should().Be(51.7);
        tiers.RestPercent.Should().Be(13.8);
    }

    [TestMethod]
    public void Signals_ReturnChartPointsMarkingNewListings()
    {
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(Listings(Day1, ["new"], views: 100, favorites: 3, createdAt: Day1.AddDays(-30), price: 25));
        rows.AddRange(Listings(Day1, ["old"], views: 100, favorites: 10, createdAt: Day1.AddDays(-300), price: 40));
        rows.AddRange(Listings(Day1, ["no-price"], views: 100, favorites: 10, createdAt: Day1.AddDays(-300)));

        var points = MarketListingSignalsCalculator.Calculate(rows).Points;

        points.Should().HaveCount(2);
        points.Single(item => item.Title == "Listing new").Should().BeEquivalentTo(new { Price = 25m, FavoritesPer30Days = 3d, AgeDays = 30, IsNew = true, Url = "https://etsy.com/listing/new" });
        points.Single(item => item.Title == "Listing old").IsNew.Should().BeFalse();
    }

    [TestMethod]
    public void Signals_ThinChartPointsToTheCap()
    {
        var ids = Enumerable.Range(0, 400).Select(index => $"l{index:D3}").ToArray();
        var rows = Listings(Day1, ids, views: 100, favorites: 2, createdAt: Day1.AddDays(-100), price: 20);

        MarketListingSignalsCalculator.Calculate(rows).Points.Count.Should().BeLessThanOrEqualTo(MarketListingSignalsCalculator.MaximumChartPoints);
    }

    [TestMethod]
    public void Supply_KeepsDailyHistoryAndCombinesItPerDay()
    {
        var a = MarketSupplyCalculator.Calculate(
        [
            Observation(new DateOnly(2026, 1, 1), 1_000),
            Observation(new DateOnly(2026, 1, 2), 1_100)
        ]);
        var b = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 2), 9_000)]);

        a.History.Should().Equal(new MarketSupplyPoint(new DateOnly(2026, 1, 1), 1_000), new MarketSupplyPoint(new DateOnly(2026, 1, 2), 1_100));
        // b joins on day 2 with a far larger count. The line follows a's own +10%, scaled to the niche's
        // current count (the median keyword, 9,000), instead of jumping because b joined the mix.
        var combined = MarketSupplyCalculator.Combine([a, b]).History;
        combined.Should().Equal(new MarketSupplyPoint(new DateOnly(2026, 1, 1), 8_182), new MarketSupplyPoint(new DateOnly(2026, 1, 2), 9_000));
    }

    [TestMethod]
    public void Supply_CombinedHistoryIgnoresKeywordsJoiningLater()
    {
        // Two large keywords are flat; a small one joins on day 2. Raw medians would show a fall.
        var large1 = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 1), 100_000), Observation(new DateOnly(2026, 1, 2), 100_000)]);
        var large2 = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 1), 80_000), Observation(new DateOnly(2026, 1, 2), 80_000)]);
        var small = MarketSupplyCalculator.Calculate([Observation(new DateOnly(2026, 1, 2), 5_000)]);

        var history = MarketSupplyCalculator.Combine([large1, large2, small]).History!;

        history.Select(point => point.ActiveListingCount).Distinct().Should().ContainSingle();
    }

    [TestMethod]
    public void Signals_UseOnlyTheMainCurrencyForPrices()
    {
        var rows = new List<MarketListingSnapshot>();
        rows.AddRange(WithCurrency(Listings(Day1, ["u1", "u2", "u3"], views: 100, favorites: 3, createdAt: Day1.AddDays(-60), price: 20), "USD"));
        rows.AddRange(WithCurrency(Listings(Day1, ["v1"], views: 100, favorites: 3, createdAt: Day1.AddDays(-60), price: 2_700_000), "VND"));

        var signals = MarketListingSignalsCalculator.Calculate(rows);

        signals.PriceCurrency.Should().Be("USD");
        signals.PriceP75.Should().Be(20);
        signals.Points.Should().HaveCount(3).And.OnlyContain(item => item.Price == 20);
        signals.SampleListingCount.Should().Be(4);
    }

    private static IEnumerable<MarketListingSnapshot> WithCurrency(IEnumerable<MarketListingSnapshot> rows, string currency) =>
        rows.Select(item => { item.Currency = currency; return item; });

    private static MarketSupplySignal Known(int count) => new(count, "unknown", null, 1, new DateOnly(2026, 1, 1));
}
