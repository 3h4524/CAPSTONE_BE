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
    decimal? TypicalPrice = null,
    int? EvidenceCount = null,
    int? DistinctListingCount = null,
    int? DistinctSellerCount = null,
    int? ObservationPeriodCount = null,
    int? RealEvidenceCount = null,
    int? SyntheticEvidenceCount = null,
    DateTime? LastObservedAt = null);

/// <summary>The explainable seller-specific ranking result.</summary>
public sealed record SellerOpportunityScore(
    double SellerFitScore,
    double FinalScore,
    IReadOnlyDictionary<string, double> Breakdown,
    string Explanation);

/// <summary>
/// Minimum scores required for an opportunity to be considered relevant to a
/// seller. Scores below these thresholds remain persisted for traceability but
/// are not exposed in the ranked opportunity list.
/// </summary>
public static class SellerOpportunityThresholds
{
    public const double MinimumSellerFitScore = 60;
    public const double MinimumFinalScore = 65;

    public static bool IsQualified(SellerOpportunityScore score) =>
        score.SellerFitScore >= MinimumSellerFitScore
        && score.FinalScore >= MinimumFinalScore;
}

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
        var evidenceQuality = EvidenceQuality(opportunity);
        var freshness = Freshness(opportunity.LastObservedAt);

        var sellerFit = Math.Round(
            excludedPenalty > 0
                ? 0
                : Math.Clamp((categoryScore * 0.4 + keywordScore * 0.35 + priceScore * 0.25) * 100, 0, 100),
            2);
        var marketScore = Math.Clamp(opportunity.MarketScore, 0, 100);
        var evidenceAdjustedMarketScore = Math.Round(marketScore * evidenceQuality.Multiplier * freshness.Multiplier, 2);
        var finalScore = Math.Round(Math.Clamp(evidenceAdjustedMarketScore * 0.6 + sellerFit * 0.4, 0, 100), 2);
        var breakdown = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["marketScore"] = marketScore,
            ["evidenceAdjustedMarketScore"] = evidenceAdjustedMarketScore,
            ["confidence"] = Math.Clamp(opportunity.Confidence, 0, 1),
            ["evidenceQualityScore"] = evidenceQuality.Score,
            ["freshnessScore"] = freshness.Score,
            ["freshnessMultiplier"] = freshness.Multiplier,
            ["freshnessAgeDays"] = freshness.AgeDays ?? 0,
            ["evidenceCount"] = opportunity.EvidenceCount ?? 0,
            ["distinctListingCount"] = opportunity.DistinctListingCount ?? 0,
            ["distinctSellerCount"] = opportunity.DistinctSellerCount ?? 0,
            ["observationPeriodCount"] = opportunity.ObservationPeriodCount ?? 0,
            ["realEvidenceCount"] = opportunity.RealEvidenceCount ?? 0,
            ["syntheticEvidenceCount"] = opportunity.SyntheticEvidenceCount ?? 0,
            ["categoryFit"] = Math.Round(categoryScore * 100, 2),
            ["keywordFit"] = Math.Round(keywordScore * 100, 2),
            ["priceFit"] = Math.Round(priceScore * 100, 2),
            ["excludedCategoryPenalty"] = Math.Round(excludedPenalty * 100, 2),
            ["sellerFitScore"] = sellerFit,
            ["finalScore"] = finalScore
        };

        var evidenceExplanation = evidenceQuality.Label == "supported" || evidenceQuality.Label == "synthetic"
            ? string.Empty
            : $" Evidence quality is {evidenceQuality.Label}; collect more real listings and observation periods before making a large investment.";
        var freshnessExplanation = freshness.Label is "fresh" or "unknown"
            ? string.Empty
            : $" Data freshness is {freshness.Label}; refresh the market snapshot before scaling.";
        var explanation = category is not null && excludedCategories.Contains(category)
            ? $"The opportunity matches an excluded category ({opportunity.Category})."
            : $"Category fit: {breakdown["categoryFit"]:0.#}/100; keyword fit: {breakdown["keywordFit"]:0.#}/100; price fit: {breakdown["priceFit"]:0.#}/100.{evidenceExplanation}{freshnessExplanation}";

        return new(sellerFit, finalScore, breakdown, explanation);
    }

    private static (string Label, double Score, double Multiplier) EvidenceQuality(MarketOpportunityCandidate opportunity)
    {
        if (opportunity.DistinctListingCount is null || opportunity.ObservationPeriodCount is null)
            return ("unknown", 1, 1);

        var realEvidence = opportunity.RealEvidenceCount ?? 0;
        var syntheticEvidence = opportunity.SyntheticEvidenceCount ?? 0;
        var totalEvidence = realEvidence + syntheticEvidence;
        // During the initial demo phase synthetic evidence keeps parity with
        // real evidence so the radar is populated. Once real evidence exists,
        // synthetic rows are deliberately down-weighted to 30%.
        var provenanceMultiplier = totalEvidence == 0 || realEvidence == 0
            ? 1
            : (realEvidence + syntheticEvidence * 0.3) / totalEvidence;
        var provenanceLabel = syntheticEvidence > 0 && realEvidence == 0
            ? "synthetic"
            : syntheticEvidence > 0
                ? "hybrid"
                : null;

        if (opportunity.DistinctListingCount < 5 || opportunity.ObservationPeriodCount < 2)
            return (provenanceLabel ?? "insufficient", 35 * provenanceMultiplier, Math.Min(0.65, provenanceMultiplier));

        if (opportunity.DistinctListingCount < 20 || opportunity.ObservationPeriodCount < 3)
            return (provenanceLabel ?? "early", 70 * provenanceMultiplier, Math.Min(0.85, provenanceMultiplier));

        return (provenanceLabel ?? "supported", 100 * provenanceMultiplier, provenanceMultiplier);
    }

    private static (string Label, int? AgeDays, double Score, double Multiplier) Freshness(DateTime? lastObservedAt)
    {
        if (lastObservedAt is null) return ("unknown", null, 0, 1);

        var ageDays = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - lastObservedAt.Value).TotalDays));
        return ageDays switch
        {
            <= 7 => ("fresh", ageDays, 100, 1),
            <= 30 => ("aging", ageDays, 70, 0.9),
            _ => ("stale", ageDays, 35, 0.75)
        };
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
