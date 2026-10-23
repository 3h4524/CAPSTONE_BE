using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Selects high-signal listings while preserving coverage across the requested niches.
/// </summary>
public static class MarketResearchSelector
{
    public static IReadOnlyList<MarketResearchSelection> Select(
        IReadOnlyCollection<CommerceMarketListing> listings,
        int topNPerNiche,
        double minimumViralScore)
    {
        ArgumentNullException.ThrowIfNull(listings);

        var topN = Math.Clamp(topNPerNiche, 1, 25);
        var minimumScore = Math.Clamp(minimumViralScore, 0, 100);

        return listings
            .Where(item => !string.IsNullOrWhiteSpace(item.ExternalListingId) && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item =>
            {
                var evaluation = ViralListingScorer.Evaluate(item, listings);
                return new MarketResearchSelection(
                    item,
                    evaluation.Score,
                    evaluation.FavoritesScore,
                    evaluation.ViewsScore,
                    evaluation.FavoritesWeight,
                    evaluation.ViewsWeight);
            })
            .Where(item => item.ViralScore >= minimumScore)
            .GroupBy(item => NormalizeNiche(item.Listing.MatchedQuery), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderByDescending(item => item.ViralScore)
                .ThenBy(item => item.Listing.ExternalListingId, StringComparer.OrdinalIgnoreCase)
                .Take(topN))
            .OrderByDescending(item => item.ViralScore)
            .ThenBy(item => NormalizeNiche(item.Listing.MatchedQuery), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Listing.ExternalListingId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeNiche(string? niche) =>
        string.IsNullOrWhiteSpace(niche) ? string.Empty : niche.Trim();
}

public sealed record MarketResearchSelection(
    CommerceMarketListing Listing,
    double ViralScore,
    double? FavoritesScore,
    double? ViewsScore,
    double FavoritesWeight,
    double ViewsWeight);
