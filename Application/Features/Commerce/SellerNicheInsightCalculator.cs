using System.Globalization;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

internal static class SellerNicheInsightCalculator
{
    public static SellerNicheMarketInsightResponse Calculate(
        string niche,
        IReadOnlyList<string> matchedQueries,
        IReadOnlyCollection<MarketListingSnapshot> snapshots,
        double relevanceScore,
        IReadOnlyList<SellerNicheKeywordInsightResponse> keywords,
        MarketSupplySignal? supply = null)
    {
        supply ??= MarketSupplySignal.Unknown;
        var aggregate = SellerMarketInsightCalculator.Calculate(niche, snapshots, [], supply);
        var eligible = MarketListingFilter.Apply(snapshots).Eligible;
        var competitorCount = LatestCompetitorCount(eligible);
        var competition = aggregate.CompetitionLevel;
        var verdict = Verdict(aggregate, competition, supply.Trend);
        var competitors = BuildCompetitors(eligible);

        // The competitor base is measured by the marketplace's active-listing count; counting
        // sellers inside a top-N sample would only measure the sample size.
        return new(
            niche,
            matchedQueries,
            verdict,
            Conclusion(verdict, aggregate, competition, supply),
            competitorCount,
            competition,
            supply.Trend,
            supply.ChangePercent,
            supply.Trend,
            supply.ChangePercent,
            aggregate.EngagementTrend,
            aggregate.EvidenceQuality,
            aggregate.ObservationPeriodCount,
            relevanceScore,
            competitors,
            keywords,
            aggregate.DemandProxy,
            supply,
            aggregate.Signals,
            aggregate.Recommendation,
            aggregate.ActionPlan,
            aggregate.Risks,
            Highlights(supply, aggregate.Signals));
    }

    /// <summary>Plain statements a seller can act on, built only from signals that have data.</summary>
    internal static IReadOnlyList<string> Highlights(MarketSupplySignal supply, MarketListingSignals signals)
    {
        var highlights = new List<string>();
        if (supply.SizePercentile is not null)
        {
            highlights.Add(
                $"Market size: at least as large as {Math.Round(supply.SizePercentile.Value).ToString(CultureInfo.InvariantCulture)}% of the tracked niches.");
        }

        if (signals.SellerCount >= 10)
        {
            highlights.Add(signals.StrongSellerCount * 10 <= signals.SellerCount
                ? $"Only {signals.StrongSellerCount} of the {signals.SellerCount} sellers at the top gain favorites quickly, so strong shops do not dominate the top yet."
                : $"{signals.StrongSellerCount} of the {signals.SellerCount} sellers at the top gain favorites quickly, so expect active competition.");
        }

        if (signals.RisingNewListingCount > 0)
        {
            highlights.Add(
                $"{signals.RisingNewListingCount} listing(s) under {MarketListingSignalsCalculator.RisingListingMaxAgeDays} days old already gain favorites quickly, so new sellers can still break in.");
        }
        else if (signals.SampleListingCount >= MarketListingSignalsCalculator.MinimumSampleListings)
        {
            highlights.Add(
                $"No listing under {MarketListingSignalsCalculator.RisingListingMaxAgeDays} days old gains favorites quickly yet, so the top favors established listings.");
        }

        if (signals.TopSellerSharePercent >= 40)
            highlights.Add($"The top 5 sellers hold {Format(signals.TopSellerSharePercent.Value)}% of the top results.");

        if (signals.PersonalizedSharePercent >= 50)
            highlights.Add($"{Format(signals.PersonalizedSharePercent.Value)}% of the top listings are personalizable, so buyers expect personalization.");

        if (signals.PriceP25 is not null && signals.PriceP75 is not null)
        {
            highlights.Add(
                $"Most top listings have a list price, before sales and discounts, between {signals.PriceP25.Value.ToString("0.00", CultureInfo.InvariantCulture)} and {signals.PriceP75.Value.ToString("0.00", CultureInfo.InvariantCulture)}{(signals.PriceCurrency is null ? string.Empty : $" {signals.PriceCurrency}")}.");
        }

        return highlights;
    }

    private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    public static SellerNicheKeywordInsightResponse CalculateKeyword(
        string keyword,
        IReadOnlyCollection<MarketListingSnapshot> snapshots,
        double? marketTrendScore,
        IReadOnlyCollection<string> profileTerms,
        MarketSupplySignal? supply = null)
    {
        supply ??= MarketSupplySignal.Unknown;
        var metrics = SellerMarketInsightCalculator.Calculate(keyword, snapshots, [], supply);
        var eligible = MarketListingFilter.Apply(snapshots).Eligible;
        var competitorCount = LatestCompetitorCount(eligible);
        var demandProxyValue = metrics.AverageFavorites is null
            ? 0
            : Math.Max(0, (int)Math.Round(metrics.AverageFavorites.Value));
        var marketTerms = SellerMarketInsightCalculator.Tokenize(
            [$"{keyword} {string.Join(' ', metrics.TopTags.Select(item => item.Tag))}"]);
        var matchedProfileTerms = marketTerms
            .Where(term => profileTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var profileMatchScore = marketTerms.Count == 0
            ? 0
            : (int)Math.Round((double)matchedProfileTerms.Length / marketTerms.Count * 100);

        return new(
            keyword,
            demandProxyValue,
            competitorCount,
            metrics.CompetitionLevel,
            KeywordScore(metrics, metrics.CompetitionLevel, marketTrendScore),
            profileMatchScore,
            matchedProfileTerms,
            marketTrendScore,
            metrics.ListingTrend,
            metrics.ListingGrowthPercent,
            metrics.EvidenceQuality,
            metrics.DemandProxy,
            supply.ActiveListingCount,
            // Chart points are drawn for the niche only; repeating them per keyword would bloat the response.
            metrics.Signals with { Points = [] },
            supply.SizePercentile);
    }

    private static int LatestCompetitorCount(IReadOnlyCollection<MarketListingSnapshot> eligible)
    {
        if (eligible.Count == 0)
            return 0;

        var latestDate = eligible.Max(item => item.ObservedAt.Date);
        return eligible
            .Where(item => item.ObservedAt.Date == latestDate)
            .Select(item => item.ExternalSellerId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string Verdict(
        SellerMarketInsightResponse aggregate,
        string competition,
        string supplyTrend) =>
        aggregate.EvidenceQuality == "insufficient"
            ? "insufficient_evidence"
            : aggregate.InsightType == "declining_market"
                ? "avoid_for_now"
                : aggregate.InsightType is "crowded_market" or "supply_outpacing_demand"
                    || (competition == "high" && supplyTrend == "growing")
                    ? "watch_and_differentiate"
                    : aggregate.InsightType == "promising_signal" && competition != "high"
                        ? "worth_following"
                        : "watch_market";

    private static string Conclusion(
        string verdict,
        SellerMarketInsightResponse aggregate,
        string competition,
        MarketSupplySignal supply)
    {
        var listings = supply.ActiveListingCount is null
            ? string.Empty
            : $" (about {supply.ActiveListingCount.Value.ToString("N0", CultureInfo.InvariantCulture)} active listings per keyword)";
        return verdict switch
        {
            "worth_following" =>
                $"This niche is worth following: demand is {aggregate.DemandProxy}, buyer engagement is " +
                $"{aggregate.EngagementTrend} and competition is {competition}{listings}.",
            "avoid_for_now" =>
                "This niche is weakening: active listings and buyer engagement are both moving down.",
            "watch_and_differentiate" =>
                $"This niche needs differentiation: competition is {competition}{listings} and active listings are {supply.Trend}.",
            "insufficient_evidence" =>
                "There is not enough collected market data to decide whether this niche is worth pursuing.",
            _ =>
                $"Keep this niche under review: competition is {competition}{listings} and demand is {aggregate.DemandProxy}."
        };
    }

    private static int KeywordScore(
        SellerMarketInsightResponse metrics,
        string competition,
        double? marketTrendScore)
    {
        var demandScore = metrics.DemandProxy switch
        {
            "high" => 90,
            "medium" => 65,
            "low" => 30,
            _ => 15
        };
        var competitionScore = competition switch
        {
            "low" => 90,
            "medium" => 60,
            "high" => 25,
            _ => 15
        };
        var trendScore = (TrendScore(metrics.ListingTrend) + TrendScore(metrics.EngagementTrend)) / 2d;
        var evidenceScore = metrics.EvidenceQuality switch
        {
            "high" => 90,
            "medium" => 65,
            "low" => 35,
            _ => 15
        };

        if (marketTrendScore is not null)
        {
            var snapshotScore = Math.Clamp(marketTrendScore.Value, 0, 100);
            return Math.Clamp((int)Math.Round(
                snapshotScore * 0.4 + demandScore * 0.25 + competitionScore * 0.2 + trendScore * 0.1 + evidenceScore * 0.05), 0, 100);
        }

        return Math.Clamp((int)Math.Round(
            demandScore * 0.35 + competitionScore * 0.25 + trendScore * 0.25 + evidenceScore * 0.15), 0, 100);
    }

    /// <summary>An unknown trend is neutral: missing history should not rank a keyword as declining.</summary>
    private static int TrendScore(string trend) => trend switch
    {
        "growing" or "up" => 90,
        "stable" or "flat" => 60,
        "reducing" or "down" => 20,
        _ => 50
    };

    private static IReadOnlyList<SellerNicheCompetitorResponse> BuildCompetitors(
        IReadOnlyCollection<MarketListingSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
            return [];

        var latestDate = snapshots.Max(item => item.ObservedAt.Date);
        // A seller's listing count inside a top-N sample changes with the sample, not with the
        // seller's catalogue, so no per-seller trend is derived from it.
        return snapshots
            .Where(item => item.ObservedAt.Date == latestDate && !string.IsNullOrWhiteSpace(item.ExternalSellerId))
            .GroupBy(item => item.ExternalSellerId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group
                    .OrderByDescending(item => item.Favorites ?? 0)
                    .ThenByDescending(item => item.Views ?? 0)
                    .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                    .First();

                return new SellerNicheCompetitorResponse(
                    group.Key,
                    group.Select(item => item.ExternalListingId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    "unknown",
                    null,
                    representative.Title,
                    representative.SourceUrl,
                    representative.Price);
            })
            .OrderByDescending(item => item.ListingCount)
            .ThenBy(item => item.SellerId, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }
}
