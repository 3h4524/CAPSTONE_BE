using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Market supply measured by the marketplace's own count of active listings for a query.
/// The count covers the whole marketplace, so unlike a sample it can be compared across days.
/// </summary>
/// <param name="ActiveListingCount">
/// The latest active-listing count. For a niche of several keywords it is the median keyword's count,
/// because the same listing can match several keywords and a sum would count it more than once.
/// </param>
/// <param name="SizePercentile">
/// Where the count ranks among all tracked queries, from near 0 (smallest) to 100 (largest).
/// Etsy matches keywords broadly, so the count is only meaningful relative to other queries.
/// </param>
public sealed record MarketSupplySignal(
    int? ActiveListingCount,
    string Trend,
    double? ChangePercent,
    int ObservationDayCount,
    DateOnly? LatestObservedOn,
    double? SizePercentile = null,
    IReadOnlyList<MarketSupplyPoint>? History = null)
{
    public static MarketSupplySignal Unknown { get; } = new(null, "unknown", null, 0, null);
}

/// <summary>The active-listing count observed on one day.</summary>
public sealed record MarketSupplyPoint(DateOnly Day, int ActiveListingCount);

internal static class MarketSupplyCalculator
{
    /// <summary>A change smaller than this, in percent, is reported as stable.</summary>
    public const double TrendThresholdPercent = 5;

    /// <summary>Below this many tracked queries a size ranking is not meaningful.</summary>
    public const int MinimumQueriesForRanking = 5;

    /// <summary>Size percentiles up to these values are low and medium competition.</summary>
    public const double LowCompetitionMaximumPercentile = 100d / 3;
    public const double MediumCompetitionMaximumPercentile = 200d / 3;

    public static MarketSupplySignal Calculate(IReadOnlyCollection<MarketQueryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var days = observations
            .Where(item => item.TotalResults is not null)
            .GroupBy(item => item.ObservedOn)
            .Select(group => new
            {
                Day = group.Key,
                Total = group.OrderByDescending(item => item.ObservedAt).First().TotalResults!.Value
            })
            .OrderBy(item => item.Day)
            .ToArray();
        if (days.Length == 0)
            return MarketSupplySignal.Unknown;

        var latest = days[^1];
        var history = days.Select(item => new MarketSupplyPoint(item.Day, item.Total)).ToArray();
        if (days.Length < 2)
            return new MarketSupplySignal(latest.Total, "unknown", null, 1, latest.Day, null, history);

        // Compare the earlier half of the observed days with the later half, so one noisy day
        // does not decide the direction on its own.
        var split = Math.Max(1, days.Length / 2);
        var earlier = days.Take(split).Average(item => item.Total);
        var later = days.Skip(split).Average(item => item.Total);
        if (earlier <= 0)
            return new MarketSupplySignal(latest.Total, "unknown", null, days.Length, latest.Day, null, history);

        var change = Math.Round((later - earlier) / earlier * 100, 2);
        return new MarketSupplySignal(latest.Total, TrendLabel(change), change, days.Length, latest.Day, null, history);
    }

    /// <summary>
    /// Adds each query's size percentile among all queries with a known active-listing count.
    /// With fewer than <see cref="MinimumQueriesForRanking"/> such queries no percentile is set.
    /// </summary>
    public static IReadOnlyDictionary<string, MarketSupplySignal> RankSizes(
        IReadOnlyDictionary<string, MarketSupplySignal> supplyByQuery)
    {
        ArgumentNullException.ThrowIfNull(supplyByQuery);
        var counts = supplyByQuery.Values
            .Where(item => item.ActiveListingCount is not null)
            .Select(item => item.ActiveListingCount!.Value)
            .Order()
            .ToArray();
        if (counts.Length < MinimumQueriesForRanking)
            return supplyByQuery;

        return supplyByQuery.ToDictionary(
            item => item.Key,
            item => item.Value.ActiveListingCount is null
                ? item.Value
                : item.Value with { SizePercentile = Percentile(counts, item.Value.ActiveListingCount.Value) },
            StringComparer.Ordinal);
    }

    /// <summary>Combines the supply of the keywords that make up one niche.</summary>
    public static MarketSupplySignal Combine(IEnumerable<MarketSupplySignal> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        var known = keywords.Where(item => item.ActiveListingCount is not null).ToArray();
        if (known.Length == 0)
            return MarketSupplySignal.Unknown;

        var counts = known.Select(item => item.ActiveListingCount!.Value).Order().ToArray();
        var percentiles = known.Where(item => item.SizePercentile is not null).Select(item => item.SizePercentile!.Value).Order().ToArray();
        var changes = known.Where(item => item.ChangePercent is not null).Select(item => item.ChangePercent!.Value).ToArray();
        double? change = changes.Length == 0 ? null : Math.Round(changes.Average(), 2);
        return new MarketSupplySignal(
            counts[counts.Length / 2],
            change is null ? "unknown" : TrendLabel(change.Value),
            change,
            known.Max(item => item.ObservationDayCount),
            known.Max(item => item.LatestObservedOn),
            percentiles.Length == 0 ? null : percentiles[percentiles.Length / 2],
            CombineHistory(known));
    }

    /// <summary>Per day, the median count of the keywords observed that day, like the niche's own count.</summary>
    private static IReadOnlyList<MarketSupplyPoint> CombineHistory(IEnumerable<MarketSupplySignal> keywords) =>
        keywords
            .SelectMany(item => item.History ?? [])
            .GroupBy(item => item.Day)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var counts = group.Select(item => item.ActiveListingCount).Order().ToArray();
                return new MarketSupplyPoint(group.Key, counts[counts.Length / 2]);
            })
            .ToArray();

    public static string Competition(double? sizePercentile) => sizePercentile switch
    {
        null => "unknown",
        <= LowCompetitionMaximumPercentile => "low",
        <= MediumCompetitionMaximumPercentile => "medium",
        _ => "high"
    };

    /// <summary>Share of tracked queries whose count is at or below <paramref name="value"/>.</summary>
    private static double Percentile(IReadOnlyList<int> sortedCounts, int value) =>
        Math.Round(sortedCounts.Count(count => count <= value) * 100d / sortedCounts.Count, 1);

    private static string TrendLabel(double change) =>
        change > TrendThresholdPercent ? "growing" : change < -TrendThresholdPercent ? "reducing" : "stable";
}
