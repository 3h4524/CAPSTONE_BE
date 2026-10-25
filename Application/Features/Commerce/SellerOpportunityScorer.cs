namespace APCS.Application.Features.Commerce;

/// <summary>Seller-owned context used by the deterministic opportunity ranking step.</summary>
public sealed record SellerResearchProfileSnapshot(
    IReadOnlyCollection<string> ProductCategories,
    IReadOnlyCollection<string> ProductKeywords,
    IReadOnlyCollection<string> PreferredCategories,
    IReadOnlyCollection<string> ExcludedCategories,
    decimal? MinimumPrice = null,
    decimal? MaximumPrice = null);

/// <summary>Market evidence and score inputs for one platform-neutral opportunity.</summary>
public sealed record MarketOpportunityCandidate(
    double MarketScore,
    double Confidence,
    string? Category,
    IReadOnlyCollection<string> Keywords,
    decimal? TypicalPrice = null);

/// <summary>The explainable seller-specific ranking result.</summary>
public sealed record SellerOpportunityScore(
    double SellerFitScore,
    double FinalScore,
    IReadOnlyDictionary<string, double> Breakdown,
    string Explanation);

/// <summary>
/// Ranks opportunities without an LLM. This keeps seller personalization stable,
/// testable and explainable before any narrative analysis is requested.
/// </summary>
public static class SellerOpportunityScorer
{
    public static SellerOpportunityScore Score(
        MarketOpportunityCandidate opportunity,
        SellerResearchProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        ArgumentNullException.ThrowIfNull(profile);

        var category = Normalize(opportunity.Category);
        var preferredCategories = NormalizeSet(profile.PreferredCategories);
        var productCategories = NormalizeSet(profile.ProductCategories);
        var excludedCategories = NormalizeSet(profile.ExcludedCategories);
        var opportunityKeywords = NormalizeSet(opportunity.Keywords);
        var productKeywords = NormalizeSet(profile.ProductKeywords);

        var categoryScore = category is null
            ? 0.5
            : excludedCategories.Contains(category)
                ? 0
                : preferredCategories.Contains(category) || productCategories.Contains(category) ? 1 : 0.35;
        var keywordScore = MatchRatio(opportunityKeywords, productKeywords);
        var priceScore = PriceFit(opportunity.TypicalPrice, profile.MinimumPrice, profile.MaximumPrice);
        var excludedPenalty = category is not null && excludedCategories.Contains(category) ? 1d : 0d;

        var sellerFit = Math.Round(
            excludedPenalty > 0
                ? 0
                : Math.Clamp((categoryScore * 0.4 + keywordScore * 0.35 + priceScore * 0.25) * 100, 0, 100),
            2);
        var marketScore = Math.Clamp(opportunity.MarketScore, 0, 100);
        var finalScore = Math.Round(Math.Clamp(marketScore * 0.6 + sellerFit * 0.4, 0, 100), 2);
        var breakdown = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketScore"] = marketScore,
            ["confidence"] = Math.Clamp(opportunity.Confidence, 0, 1),
            ["categoryFit"] = Math.Round(categoryScore * 100, 2),
            ["keywordFit"] = Math.Round(keywordScore * 100, 2),
            ["priceFit"] = Math.Round(priceScore * 100, 2),
            ["excludedCategoryPenalty"] = Math.Round(excludedPenalty * 100, 2),
            ["sellerFitScore"] = sellerFit,
            ["finalScore"] = finalScore
        };

        var explanation = category is not null && excludedCategories.Contains(category)
            ? $"The opportunity matches an excluded category ({opportunity.Category})."
            : $"Category fit: {breakdown["categoryFit"]:0.#}/100; keyword fit: {breakdown["keywordFit"]:0.#}/100; price fit: {breakdown["priceFit"]:0.#}/100.";

        return new(sellerFit, finalScore, breakdown, explanation);
    }

    private static double PriceFit(decimal? price, decimal? minimum, decimal? maximum)
    {
        if (price is null || (minimum is null && maximum is null)) return 0.5;
        if (minimum is not null && price < minimum) return 0;
        if (maximum is not null && price > maximum) return 0;
        return 1;
    }

    private static double MatchRatio(IReadOnlySet<string> source, IReadOnlySet<string> target)
    {
        if (source.Count == 0 || target.Count == 0) return 0;
        return (double)source.Intersect(target, StringComparer.Ordinal).Count() / source.Count;
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> values) =>
        values.Select(Normalize).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
