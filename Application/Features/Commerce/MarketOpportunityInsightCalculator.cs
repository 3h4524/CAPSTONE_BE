using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

internal sealed record MarketOpportunityEvidenceInput(
    string ExternalListingId,
    string? ExternalSellerId,
    string Title,
    string? SourceUrl,
    decimal? Price,
    string? Currency,
    int? Favorites,
    int? Views,
    DateTime ObservedAt,
    bool IsSynthetic = false);

internal static class MarketOpportunityInsightCalculator
{
    public static MarketOpportunityInsights Calculate(
        MarketOpportunity opportunity,
        IReadOnlyCollection<MarketOpportunityEvidenceInput> evidence,
        DateTime? nowUtc = null)
    {
        var latestByListing = evidence
            .GroupBy(item => item.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ObservedAt).First())
            .ToArray();
        var prices = latestByListing.Where(item => item.Price is not null).Select(item => item.Price!.Value).OrderBy(item => item).ToArray();
        var engagementRate = CalculateEngagement(latestByListing);
        var engagementChange = CalculateEngagementChange(evidence);
        var observationPeriodCount = evidence.Select(item => item.ObservedAt.Date).Distinct().Count();
        var evidenceQuality = EvidenceQualityLabel(latestByListing.Length, observationPeriodCount);

        var firstObservedAt = evidence.Count == 0 ? opportunity.FirstObservedAt : evidence.Min(item => item.ObservedAt);
        var lastObservedAt = evidence.Count == 0 ? opportunity.LastObservedAt : evidence.Max(item => item.ObservedAt);
        var syntheticCount = evidence.Count(item => item.IsSynthetic);
        var realCount = evidence.Count - syntheticCount;
        var dataSource = evidence.Count == 0
            ? "unknown"
            : syntheticCount == 0
                ? "real"
            : syntheticCount == evidence.Count
                ? "synthetic"
                : "hybrid";
        var freshness = Freshness(lastObservedAt, nowUtc ?? DateTime.UtcNow);

        return new(
            evidence.Count,
            latestByListing.Length,
            latestByListing.Select(item => item.ExternalSellerId).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Median(prices),
            prices.FirstOrDefaultOrNull(),
            prices.LastOrDefaultOrNull(),
            engagementRate,
            engagementChange,
            TrendLabel(engagementChange),
            CompetitionLabel(latestByListing.Length, evidenceQuality),
            ConfidenceLabel((double)opportunity.Confidence, evidenceQuality),
            evidenceQuality,
            prices.Length < 5 ? "observed" : "typical",
            observationPeriodCount,
            firstObservedAt,
            lastObservedAt,
            dataSource,
            realCount,
            syntheticCount,
            freshness.Label,
            freshness.AgeDays,
            freshness.Score,
            ParseBreakdown(opportunity.ScoreBreakdown),
            latestByListing
                .OrderByDescending(item => item.ObservedAt)
                .Take(5)
                .Select(item => new MarketOpportunityEvidenceResponse(item.Title, item.SourceUrl, item.Price, item.Currency, item.Favorites, item.Views, item.ObservedAt))
                .ToArray());
    }

    private static double? CalculateEngagement(IReadOnlyCollection<MarketOpportunityEvidenceInput> items)
    {
        var withViews = items.Where(item => item.Views is > 0 && item.Favorites is >= 0).ToArray();
        if (withViews.Length == 0) return null;
        return Math.Round(withViews.Sum(item => (double)item.Favorites!.Value) / withViews.Sum(item => item.Views!.Value) * 100, 2);
    }

    private static double? CalculateEngagementChange(IReadOnlyCollection<MarketOpportunityEvidenceInput> evidence)
    {
        if (evidence.Count < 2) return null;
        var ordered = evidence.OrderBy(item => item.ObservedAt).ToArray();
        var midpoint = ordered[0].ObservedAt + (ordered[^1].ObservedAt - ordered[0].ObservedAt) / 2;
        var first = CalculateEngagement(ordered.Where(item => item.ObservedAt <= midpoint).ToArray());
        var last = CalculateEngagement(ordered.Where(item => item.ObservedAt > midpoint).ToArray());
        if (first is null || last is null || first == 0) return null;
        return Math.Round((last.Value - first.Value) / first.Value * 100, 2);
    }

    private static decimal? Median(IReadOnlyList<decimal> values) => values.Count switch
    {
        0 => null,
        _ when values.Count % 2 == 1 => values[values.Count / 2],
        _ => (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2
    };

    private static string TrendLabel(double? change) => change switch
    {
        null => "unknown",
        > 5 => "up",
        < -5 => "down",
        _ => "flat"
    };

    private static string CompetitionLabel(int listingCount, string evidenceQuality) => evidenceQuality == "insufficient"
        ? "unknown"
        : listingCount switch
    {
        <= 10 => "low",
        <= 30 => "medium",
        _ => "high"
    };

    private static string ConfidenceLabel(double confidence, string evidenceQuality) => evidenceQuality == "insufficient"
        ? "low"
        : confidence switch
    {
        >= 0.8 => "high",
        >= 0.5 => "medium",
        _ => "low"
    };

    private static string EvidenceQualityLabel(int listingCount, int observationPeriodCount) => listingCount < 5 || observationPeriodCount < 2
        ? "insufficient"
        : listingCount < 20 || observationPeriodCount < 3
            ? "early"
            : "supported";

    private static (string Label, int? AgeDays, int Score) Freshness(DateTime lastObservedAt, DateTime nowUtc)
    {
        if (lastObservedAt == default || lastObservedAt > nowUtc)
            return ("unknown", null, 0);

        var ageDays = Math.Max(0, (int)Math.Floor((nowUtc - lastObservedAt).TotalDays));
        return ageDays switch
        {
            <= 7 => ("fresh", ageDays, 100),
            <= 30 => ("aging", ageDays, 70),
            _ => ("stale", ageDays, 35)
        };
    }

    private static IReadOnlyDictionary<string, double> ParseBreakdown(string value)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(value);
            return document.RootElement.EnumerateObject()
                .Where(property => property.Value.ValueKind is System.Text.Json.JsonValueKind.Number)
                .ToDictionary(property => property.Name, property => property.Value.GetDouble(), StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, double>();
        }
    }
}

file static class DecimalCollectionExtensions
{
    public static decimal? FirstOrDefaultOrNull(this IReadOnlyList<decimal> values) => values.Count == 0 ? null : values[0];
    public static decimal? LastOrDefaultOrNull(this IReadOnlyList<decimal> values) => values.Count == 0 ? null : values[^1];
}
