using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Turns the collected public listings of one query into market signals.
/// </summary>
/// <remarks>
/// A collection run samples only the top results of a search, and that set changes from day to
/// day. Signals therefore never compare two different samples: supply comes from the marketplace's
/// own active-listing count, engagement from the change of the same listings between observations,
/// and demand from favorites normalised by each listing's age.
/// </remarks>
internal static class SellerMarketInsightCalculator
{
    /// <summary>Demand bands for the median favorites per 30 days, near the 75th and 50th niche percentiles.</summary>
    public const double HighDemandFavoritesPer30Days = 1;
    public const double MediumDemandFavoritesPer30Days = 0.25;

    /// <summary>Listings that must have view intervals in both halves of the compared period.</summary>
    public const int MinimumPairedListings = 3;

    /// <summary>A view-velocity change smaller than this, in percent, is reported as flat.</summary>
    public const double EngagementThresholdPercent = 10;

    public static SellerMarketInsightResponse Calculate(
        string query,
        IReadOnlyCollection<MarketListingSnapshot> snapshots,
        IReadOnlyCollection<string> profileTerms,
        MarketSupplySignal? supply = null)
    {
        supply ??= MarketSupplySignal.Unknown;
        var metrics = MarketQueryMetricsCalculator.Calculate(query, snapshots);
        var eligible = MarketListingFilter.Apply(snapshots).Eligible;
        var signals = MarketListingSignalsCalculator.Calculate(eligible);
        var (engagementTrend, engagementChange) = CalculateEngagementTrend(eligible);
        var relevance = CalculateRelevance(query, metrics.TopTags, profileTerms);
        DateTime? first = eligible.Count == 0 ? null : eligible.Min(item => item.ObservedAt);
        DateTime? last = eligible.Count == 0 ? null : eligible.Max(item => item.ObservedAt);
        var demandProxy = DemandProxy(signals);
        var competition = MarketSupplyCalculator.Competition(supply.SizePercentile);
        var evidenceQuality = EvidenceQuality(
            signals.SampleListingCount,
            Math.Max(metrics.ObservationPeriodCount, supply.ObservationDayCount));
        var seasonality = Seasonality(eligible);
        var action = BuildActionableInsight(
            supply.Trend,
            engagementTrend,
            demandProxy,
            competition,
            evidenceQuality,
            seasonality,
            signals);

        return new(
            query,
            metrics.DistinctListingCount,
            metrics.DistinctSellerCount,
            metrics.MedianPrice,
            metrics.AverageFavorites,
            metrics.AverageViews,
            metrics.NewListingCount,
            metrics.ObservationPeriodCount,
            first,
            last,
            supply.Trend,
            supply.ChangePercent,
            engagementTrend,
            engagementChange,
            demandProxy,
            competition,
            evidenceQuality,
            seasonality,
            metrics.TopTags,
            relevance,
            action.InsightType,
            action.Recommendation,
            action.ActionPlan,
            action.Risks,
            supply.ActiveListingCount,
            signals);
    }

    /// <summary>
    /// Compares how fast the same listings gain views in the earlier and the later half of the
    /// observed days. Only listings with view intervals in both halves take part, and an interval
    /// where the views counter went back is ignored, so different listings are never compared.
    /// </summary>
    internal static (string Trend, double? ChangePercent) CalculateEngagementTrend(
        IReadOnlyCollection<MarketListingSnapshot> snapshots)
    {
        var intervals = new List<(string ListingId, DateTime EndDate, double ViewsPerDay)>();
        foreach (var listing in snapshots
                     .Where(item => item.Views is not null)
                     .GroupBy(item => item.ExternalListingId.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var daily = listing
                .GroupBy(item => item.ObservedAt.Date)
                .Select(day => day.OrderByDescending(item => item.ObservedAt).First())
                .OrderBy(item => item.ObservedAt)
                .ToArray();
            for (var index = 1; index < daily.Length; index++)
            {
                var days = (daily[index].ObservedAt - daily[index - 1].ObservedAt).TotalDays;
                var gain = daily[index].Views!.Value - daily[index - 1].Views!.Value;
                if (days <= 0 || gain < 0)
                    continue;

                intervals.Add((listing.Key, daily[index].ObservedAt.Date, gain / days));
            }
        }

        var endDates = intervals.Select(item => item.EndDate).Distinct().Order().ToArray();
        if (endDates.Length < 2)
            return ("unknown", null);

        var split = endDates[Math.Max(1, endDates.Length / 2)];
        var paired = intervals
            .GroupBy(item => item.ListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Earlier = group.Where(item => item.EndDate < split).Select(item => item.ViewsPerDay).ToArray(),
                Later = group.Where(item => item.EndDate >= split).Select(item => item.ViewsPerDay).ToArray()
            })
            .Where(item => item.Earlier.Length > 0 && item.Later.Length > 0)
            .ToArray();
        if (paired.Length < MinimumPairedListings)
            return ("unknown", null);

        var earlierRate = paired.Sum(item => item.Earlier.Average());
        var laterRate = paired.Sum(item => item.Later.Average());
        if (earlierRate <= 0)
            return ("unknown", null);

        var change = Math.Round((laterRate - earlierRate) / earlierRate * 100, 2);
        return (change > EngagementThresholdPercent ? "up" : change < -EngagementThresholdPercent ? "down" : "flat", change);
    }

    internal static string DemandProxy(MarketListingSignals signals) =>
        signals.SampleListingCount < MarketListingSignalsCalculator.MinimumSampleListings
            || signals.MedianFavoritesPer30Days is null
            ? "insufficient"
            : signals.MedianFavoritesPer30Days >= HighDemandFavoritesPer30Days
                ? "high"
                : signals.MedianFavoritesPer30Days >= MediumDemandFavoritesPer30Days
                    ? "medium"
                    : "low";

    internal static string EvidenceQuality(int listingCount, int observationDayCount) =>
        listingCount < MarketListingSignalsCalculator.MinimumSampleListings
            ? "insufficient"
            : listingCount >= 20 && observationDayCount >= 7
                ? "high"
                : listingCount >= 20 || observationDayCount >= 3
                    ? "medium"
                    : "low";

    private static string Seasonality(IReadOnlyCollection<MarketListingSnapshot> snapshots)
    {
        var dates = snapshots.Select(item => item.ObservedAt.Date).Distinct().Order().ToArray();
        if (dates.Length < 4 || dates[^1].Subtract(dates[0]).TotalDays < 28)
            return "insufficient";

        var monthlyCounts = snapshots
            .GroupBy(item => new { item.ObservedAt.Year, item.ObservedAt.Month })
            .Select(group => group.Count())
            .ToArray();
        var average = monthlyCounts.Average();
        return monthlyCounts.Max() >= average * 1.5 ? "seasonal" : "stable";
    }

    private static ActionableMarketInsight BuildActionableInsight(
        string supplyTrend,
        string engagementTrend,
        string demandProxy,
        string competition,
        string evidenceQuality,
        string seasonality,
        MarketListingSignals signals)
    {
        if (evidenceQuality == "insufficient")
        {
            return new(
                "insufficient_evidence",
                "Collect more observations before committing budget to this keyword.",
                [
                    "Collect this keyword once per day so at least five listings and the marketplace total are observed.",
                    "Compare the same listings again after a few days to read engagement.",
                    "Use a small listing test only after the signal is confirmed."
                ],
                ["The current sample is too small to describe this market."]);
        }

        if (supplyTrend == "reducing" && engagementTrend == "down")
        {
            return new(
                "declining_market",
                "Avoid a broad launch for now; both supply and engagement are weakening.",
                [
                    "Review adjacent keywords with growing listings or engagement.",
                    "Keep this keyword on watch instead of investing in a large catalogue.",
                    "Re-check the signal after the next collection cycle."
                ],
                ["The keyword may be losing market attention."]);
        }

        if (competition == "high" && demandProxy is "low" or "insufficient")
        {
            return new(
                "crowded_market",
                "Differentiate before entering; the market is crowded without a strong demand signal.",
                [
                    "Narrow the keyword with a specific audience, use case or personalization angle.",
                    "Compare the top tags and price range before designing the first listing.",
                    "Run a small differentiated test rather than copying the broad keyword."
                ],
                ["High listing competition may make organic discovery difficult."]);
        }

        if (supplyTrend == "growing" && (engagementTrend == "down" || demandProxy == "low"))
        {
            return new(
                "supply_outpacing_demand",
                "Active listings are increasing faster than buyer interest; validate differentiation before launching.",
                [
                    "Use a narrower long-tail keyword and a clear buyer use case.",
                    "Test one differentiated listing with a controlled price point.",
                    "Monitor engagement again before expanding the collection."
                ],
                ["New supply may be entering faster than buyer interest."]);
        }

        var hasRisingNewListings = signals.RisingNewListingCount > 0;
        if (competition != "high"
            && (engagementTrend == "up" || demandProxy == "high" || (demandProxy == "medium" && hasRisingNewListings)))
        {
            var firstStep = seasonality == "seasonal"
                ? "Prepare the listing and inventory before the seasonal peak."
                : hasRisingNewListings
                    ? $"Study the {signals.RisingNewListingCount} listing(s) under {MarketListingSignalsCalculator.RisingListingMaxAgeDays} days old that already gain favorites quickly."
                    : "Test one focused listing while buyer interest is positive.";
            return new(
                "promising_signal",
                "This keyword shows a promising demand signal with manageable competition.",
                [
                    firstStep,
                    "Use the leading tags as discovery terms without copying competitor wording.",
                    "Re-check price and engagement after the first test listing."
                ],
                ["The signal is based on public engagement proxies, not sales data."]);
        }

        return new(
            "watch_market",
            "Keep this keyword under observation and validate it with a focused listing test.",
            [
                "Choose a specific audience or use case for the first listing.",
                "Compare the observed price range and competition before publishing.",
                "Collect another observation period before scaling the idea."
            ],
            ["The current signal is not strong enough for an aggressive launch."]);
    }

    private static double CalculateRelevance(
        string query,
        IReadOnlyCollection<MarketTagMetric> topTags,
        IReadOnlyCollection<string> profileTerms)
    {
        if (profileTerms.Count == 0)
            return 1;

        var marketTerms = Tokenize([$"{query} {string.Join(' ', topTags.Select(item => item.Tag))}"]);
        var matched = profileTerms.Count(term => marketTerms.Contains(term));
        return Math.Round((double)matched / profileTerms.Count, 3);
    }

    public static IReadOnlyCollection<string> Tokenize(IEnumerable<string> values) =>
        values
            .SelectMany(value => value.Split([' ', ',', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private sealed record ActionableMarketInsight(
        string InsightType,
        string Recommendation,
        IReadOnlyList<string> ActionPlan,
        IReadOnlyList<string> Risks);
}
