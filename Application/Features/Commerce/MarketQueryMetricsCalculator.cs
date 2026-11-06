using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

public sealed record MarketTagMetric(string Tag, int ListingCount);

public sealed record MarketQueryMetrics(
    string Query,
    int RawSnapshotCount,
    int EligibleSnapshotCount,
    int RejectedSnapshotCount,
    int ObservationPeriodCount,
    int DistinctListingCount,
    int DistinctSellerCount,
    decimal? MedianPrice,
    double? AverageFavorites,
    double? AverageViews,
    int NewListingCount,
    IReadOnlyList<MarketTagMetric> TopTags,
    DateTime? FirstObservedAt,
    DateTime? LastObservedAt,
    string Confidence,
    IReadOnlyDictionary<string, int> RejectionReasons);

internal static class MarketQueryMetricsCalculator
{
    public static MarketQueryMetrics Calculate(
        string query,
        IReadOnlyCollection<MarketListingSnapshot> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(snapshots);

        var filtered = MarketListingFilter.Apply(snapshots);
        var eligible = filtered.Eligible;
        var latestByListing = eligible
            .GroupBy(snapshot => snapshot.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(snapshot => snapshot.ObservedAt)
                .ThenByDescending(snapshot => snapshot.CollectedAt)
                .First())
            .ToArray();
        var observationDates = eligible.Select(snapshot => snapshot.ObservedAt.Date).Distinct().Order().ToArray();
        var latestDate = observationDates.LastOrDefault();
        var firstSeenByListing = eligible
            .GroupBy(snapshot => snapshot.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Min(snapshot => snapshot.ObservedAt.Date))
            .ToArray();

        var prices = latestByListing
            .Where(snapshot => snapshot.Price is not null)
            .Select(snapshot => snapshot.Price!.Value)
            .Order()
            .ToArray();
        var favorites = latestByListing
            .Where(snapshot => snapshot.Favorites is not null)
            .Select(snapshot => (double)snapshot.Favorites!.Value)
            .ToArray();
        var views = latestByListing
            .Where(snapshot => snapshot.Views is not null)
            .Select(snapshot => (double)snapshot.Views!.Value)
            .ToArray();

        return new(
            query,
            snapshots.Count,
            eligible.Count,
            filtered.RejectedCount,
            observationDates.Length,
            latestByListing.Length,
            latestByListing
                .Select(snapshot => snapshot.ExternalSellerId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Median(prices),
            Average(favorites),
            Average(views),
            latestByListing.Length == 0
                ? 0
                : firstSeenByListing.Count(date => date == latestDate),
            ExtractTopTags(latestByListing),
            eligible.Count == 0 ? null : eligible.Min(snapshot => snapshot.ObservedAt),
            eligible.Count == 0 ? null : eligible.Max(snapshot => snapshot.ObservedAt),
            Confidence(latestByListing.Length, observationDates.Length),
            filtered.RejectionReasons);
    }

    private static IReadOnlyList<MarketTagMetric> ExtractTopTags(IEnumerable<MarketListingSnapshot> snapshots)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            foreach (var tag in ReadTags(snapshot.Tags).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var normalized = tag.Trim().ToLowerInvariant();
                if (normalized.Length > 0)
                    counts[normalized] = counts.GetValueOrDefault(normalized) + 1;
            }
        }

        return counts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(10)
            .Select(item => new MarketTagMetric(item.Key, item.Value))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static decimal? Median(IReadOnlyList<decimal> values) => values.Count switch
    {
        0 => null,
        _ when values.Count % 2 == 1 => values[values.Count / 2],
        _ => (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2
    };

    private static double? Average(IReadOnlyList<double> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static string Confidence(int listingCount, int observationPeriodCount) =>
        listingCount < 5 || observationPeriodCount < 2
            ? "low"
            : listingCount < 20 || observationPeriodCount < 3
                ? "medium"
                : "high";
}
