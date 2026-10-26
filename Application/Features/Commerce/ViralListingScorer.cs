using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

/// <summary>Ranks a batch using only metrics supplied by the marketplace.</summary>
public static class ViralListingScorer
{
    public static double Score(CommerceMarketListing listing, IReadOnlyCollection<CommerceMarketListing> batch)
        => Evaluate(listing, batch).Score;

    public static (double Score, double? FavoritesScore, double? ViewsScore, double FavoritesWeight, double ViewsWeight) Evaluate(
        CommerceMarketListing listing,
        IReadOnlyCollection<CommerceMarketListing> batch)
    {
        var favoriteValues = batch.Where(item => item.Favorites is not null).Select(item => LogCount(item.Favorites!.Value)).ToArray();
        var viewValues = batch.Where(item => item.Views is not null).Select(item => LogCount(item.Views!.Value)).ToArray();
        var favoriteScore = Normalize(listing.Favorites, favoriteValues);
        var viewScore = Normalize(listing.Views, viewValues);

        // Keep the score honest when a platform omits one metric. No unavailable metric is fabricated.
        var weight = (listing.Favorites is not null ? 0.55 : 0) + (listing.Views is not null ? 0.45 : 0);
        if (weight == 0)
            return (0, null, null, 0, 0);

        return (
            Math.Round(((listing.Favorites is not null ? favoriteScore * 0.55 : 0)
                + (listing.Views is not null ? viewScore * 0.45 : 0)) / weight * 100, 2),
            listing.Favorites is not null ? Math.Round(favoriteScore * 100, 2) : null,
            listing.Views is not null ? Math.Round(viewScore * 100, 2) : null,
            listing.Favorites is not null ? 0.55 / weight : 0,
            listing.Views is not null ? 0.45 / weight : 0);
    }

    private static double Normalize(int? value, IReadOnlyCollection<double> values)
    {
        if (value is null || values.Count == 0)
            return 0;

        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 0.0001)
            return 1;

        return (LogCount(value.Value) - min) / (max - min);
    }

    private static double LogCount(int value) => Math.Log(1d + Math.Max(0, value));
}
