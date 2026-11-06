using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Signals read from the latest collection of public listings of each query. Each one is
/// normalised per listing (for example favorites per 30 days of listing age), so it does not
/// depend on how many times or how deep the market was sampled.
/// </summary>
public sealed record MarketListingSignals(
    int SampleListingCount,
    int? MedianListingAgeDays,
    double? MedianFavoritesPer30Days,
    double? NewListingSharePercent,
    int RisingNewListingCount,
    double? PersonalizedSharePercent,
    decimal? PriceP25,
    decimal? MedianPrice,
    decimal? PriceP75,
    double? TopSellerSharePercent,
    int SellerCount,
    int StrongSellerCount,
    IReadOnlyList<MarketRisingListing> RisingListings,
    DateOnly? LatestCollectedOn,
    IReadOnlyList<MarketListingPoint> Points,
    MarketShareTiers? ShareTiers,
    string? PriceCurrency)
{
    public static MarketListingSignals Empty { get; } =
        new(0, null, null, null, 0, null, null, null, null, null, 0, 0, [], null, [], null, null);
}

/// <summary>One top listing as a chart point: its price against how fast it gains favorites.</summary>
public sealed record MarketListingPoint(
    string Title,
    decimal Price,
    double FavoritesPer30Days,
    int AgeDays,
    bool IsNew,
    string? Url);

/// <summary>How the top listings split between the largest shops and everyone else, in percent.</summary>
public sealed record MarketShareTiers(
    double TopFivePercent,
    double NextFifteenPercent,
    double RestPercent);

/// <summary>A public listing under 90 days old that already gains favorites quickly.</summary>
public sealed record MarketRisingListing(
    string Title,
    string? Url,
    int AgeDays,
    double FavoritesPer30Days,
    decimal? Price,
    string? Currency);

internal static class MarketListingSignalsCalculator
{
    /// <summary>A listing younger than this counts as a new market entrant.</summary>
    public const int NewListingMaxAgeDays = 30;

    /// <summary>A listing younger than this with traction counts as a rising new listing.</summary>
    public const int RisingListingMaxAgeDays = 90;

    /// <summary>
    /// Favorites per 30 days that count as traction: roughly the 75th percentile across collected
    /// Etsy listings. A seller with at least one such listing counts as a strong seller.
    /// </summary>
    public const double TractionFavoritesPer30Days = 3;

    /// <summary>
    /// Favorites per 30 days divides by at least this many days. A listing a few days old with a
    /// couple of favorites would otherwise extrapolate to a rate it has not shown yet.
    /// </summary>
    public const int MinimumRateAgeDays = 14;

    /// <summary>Below this many listings a share or median says little about the market.</summary>
    public const int MinimumSampleListings = 5;

    private const int TopSellerCount = 5;
    private const int NextSellerCount = 15;
    private const int MaximumRisingListings = 5;

    /// <summary>A chart stays readable and the response small with this many points.</summary>
    public const int MaximumChartPoints = 150;

    public static MarketListingSignals Calculate(IReadOnlyCollection<MarketListingSnapshot> eligible)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        var latest = LatestCollection(eligible)
            .GroupBy(item => item.ExternalListingId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.ObservedAt)
                .ThenByDescending(item => item.CollectedAt)
                .First())
            .ToArray();
        if (latest.Length == 0)
            return MarketListingSignals.Empty;

        var listings = latest.Select(Describe).ToArray();
        var aged = listings.Where(item => item.AgeDays is not null).ToArray();
        var velocities = listings
            .Where(item => item.FavoritesPer30Days is not null)
            .Select(item => item.FavoritesPer30Days!.Value)
            .ToArray();
        var personalizable = listings.Where(item => item.Personalizable is not null).ToArray();
        // Etsy returns each price in its shop's currency. Prices in different currencies cannot be
        // compared without exchange rates, so price signals use the sample's main currency only.
        var priceCurrency = MainCurrency(latest);
        var prices = latest
            .Where(item => item.Price is not null && SameCurrency(item.Currency, priceCurrency))
            .Select(item => item.Price!.Value)
            .Order()
            .ToArray();
        var rising = listings
            .Where(item => item.AgeDays <= RisingListingMaxAgeDays
                && item.FavoritesPer30Days >= TractionFavoritesPer30Days)
            .OrderByDescending(item => item.FavoritesPer30Days)
            .ToArray();
        var sellers = listings
            .Where(item => !string.IsNullOrWhiteSpace(item.Snapshot.ExternalSellerId))
            .GroupBy(item => item.Snapshot.ExternalSellerId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var enoughListings = latest.Length >= MinimumSampleListings;

        return new MarketListingSignals(
            latest.Length,
            aged.Length == 0 ? null : (int)Math.Round(Median(aged.Select(item => item.AgeDays!.Value).ToArray())!.Value),
            velocities.Length == 0 ? null : Math.Round(Median(velocities)!.Value, 2),
            enoughListings && aged.Length > 0
                ? Percent(aged.Count(item => item.AgeDays <= NewListingMaxAgeDays), aged.Length)
                : null,
            rising.Length,
            enoughListings && personalizable.Length > 0
                ? Percent(personalizable.Count(item => item.Personalizable == true), personalizable.Length)
                : null,
            Quantile(prices, 0.25m),
            Quantile(prices, 0.5m),
            Quantile(prices, 0.75m),
            enoughListings ? TopSellerShare(sellers) : null,
            sellers.Length,
            sellers.Count(group => group.Any(item => item.FavoritesPer30Days >= TractionFavoritesPer30Days)),
            rising
                .Take(MaximumRisingListings)
                .Select(item => new MarketRisingListing(
                    item.Snapshot.Title,
                    item.Snapshot.SourceUrl,
                    (int)Math.Round(item.AgeDays!.Value),
                    Math.Round(item.FavoritesPer30Days!.Value, 1),
                    item.Snapshot.Price,
                    item.Snapshot.Currency))
                .ToArray(),
            DateOnly.FromDateTime(latest.Max(item => item.ObservedAt)),
            ChartPoints(listings, priceCurrency),
            enoughListings ? ShareTiers(sellers) : null,
            priceCurrency);
    }

    /// <summary>The most common currency among priced listings; ties go to the alphabetically first code.</summary>
    private static string? MainCurrency(IEnumerable<MarketListingSnapshot> listings) =>
        listings
            .Where(item => item.Price is not null && !string.IsNullOrWhiteSpace(item.Currency))
            .GroupBy(item => item.Currency!.Trim().ToUpperInvariant())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();

    private static bool SameCurrency(string? currency, string? mainCurrency) =>
        mainCurrency is null || string.Equals(currency?.Trim(), mainCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Listings with a price and an age, thinned to an even, deterministic sample when a niche
    /// spans several keywords, so the scatter shows the whole spread instead of one end of it.
    /// </summary>
    private static IReadOnlyList<MarketListingPoint> ChartPoints(IReadOnlyList<ListingFacts> listings, string? priceCurrency)
    {
        var plotted = listings
            .Where(item => item.Snapshot.Price is not null
                && SameCurrency(item.Snapshot.Currency, priceCurrency)
                && item.FavoritesPer30Days is not null
                && item.AgeDays is not null)
            .OrderBy(item => item.Snapshot.ExternalListingId, StringComparer.Ordinal)
            .ToArray();
        var stride = Math.Max(1, (int)Math.Ceiling(plotted.Length / (double)MaximumChartPoints));
        return plotted
            .Where((_, index) => index % stride == 0)
            .Select(item => new MarketListingPoint(
                item.Snapshot.Title,
                item.Snapshot.Price!.Value,
                Math.Round(item.FavoritesPer30Days!.Value, 2),
                (int)Math.Round(item.AgeDays!.Value),
                item.AgeDays <= RisingListingMaxAgeDays,
                item.Snapshot.SourceUrl))
            .ToArray();
    }

    private static MarketShareTiers? ShareTiers(IReadOnlyCollection<IGrouping<string, ListingFacts>> sellers)
    {
        var counts = sellers.Select(group => group.Count()).OrderDescending().ToArray();
        var total = counts.Sum();
        if (total == 0)
            return null;

        var top = Percent(counts.Take(TopSellerCount).Sum(), total);
        var next = Percent(counts.Skip(TopSellerCount).Take(NextSellerCount).Sum(), total);
        return new MarketShareTiers(top, next, Math.Round(100 - top - next, 1));
    }

    /// <summary>
    /// Keeps each query's most recent collection day. A listing that dropped out of the top
    /// results is not observed again, so older days would otherwise keep inflating the sample.
    /// </summary>
    private static IEnumerable<MarketListingSnapshot> LatestCollection(IEnumerable<MarketListingSnapshot> snapshots) =>
        snapshots
            .GroupBy(item => MarketQueryKey.Normalize(item.MatchedQuery), StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var latestDay = group.Max(item => item.ObservedAt.Date);
                return group.Where(item => item.ObservedAt.Date == latestDay);
            });

    private static ListingFacts Describe(MarketListingSnapshot snapshot)
    {
        var (createdAtUtc, personalizable) = ReadPayload(snapshot.RawPayload);
        double? ageDays = null;
        if (createdAtUtc is not null && createdAtUtc.Value <= snapshot.ObservedAt)
            ageDays = Math.Max(1, (snapshot.ObservedAt - createdAtUtc.Value).TotalDays);

        double? favoritesPer30Days = ageDays is null || snapshot.Favorites is null
            ? null
            : snapshot.Favorites.Value * 30d / Math.Max(MinimumRateAgeDays, ageDays.Value);
        return new ListingFacts(snapshot, ageDays, favoritesPer30Days, personalizable);
    }

    private static (DateTime? CreatedAtUtc, bool? Personalizable) ReadPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (null, null);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            DateTime? createdAtUtc = root.TryGetProperty("original_creation_timestamp", out var created)
                && created.ValueKind == JsonValueKind.Number
                && created.TryGetInt64(out var seconds)
                && seconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                    : null;
            bool? personalizable = root.TryGetProperty("is_personalizable", out var flag)
                && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? flag.GetBoolean()
                    : null;
            return (createdAtUtc, personalizable);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static double? TopSellerShare(IReadOnlyCollection<IGrouping<string, ListingFacts>> sellers)
    {
        var counts = sellers.Select(group => group.Count()).OrderDescending().ToArray();
        var total = counts.Sum();
        return total == 0 ? null : Percent(counts.Take(TopSellerCount).Sum(), total);
    }

    private static double Percent(int part, int total) => Math.Round(part * 100d / total, 1);

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;

        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>Linear-interpolated quantile of already sorted values.</summary>
    private static decimal? Quantile(IReadOnlyList<decimal> sorted, decimal quantile)
    {
        if (sorted.Count == 0)
            return null;

        var position = (sorted.Count - 1) * quantile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var value = sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        return Math.Round(value, 2);
    }

    private sealed record ListingFacts(
        MarketListingSnapshot Snapshot,
        double? AgeDays,
        double? FavoritesPer30Days,
        bool? Personalizable);
}
