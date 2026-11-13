using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

public sealed record SellerResearchProfileRequest(
    string? ShopGoal,
    IReadOnlyCollection<string>? TargetAudiences,
    IReadOnlyCollection<string>? PreferredCategories,
    IReadOnlyCollection<string>? ExcludedCategories,
    IReadOnlyCollection<string>? PreferredKeywords,
    decimal? MinimumPrice,
    decimal? MaximumPrice);

public sealed record SellerResearchProfileResponse(
    Guid UserId,
    string? ShopGoal,
    IReadOnlyList<string> TargetAudiences,
    IReadOnlyList<string> PreferredCategories,
    IReadOnlyList<string> ExcludedCategories,
    IReadOnlyList<string> PreferredKeywords,
    decimal? MinimumPrice,
    decimal? MaximumPrice,
    int ProfileVersion,
    DateTime UpdatedAt);

public sealed record SellerOpportunityResponse(
    Guid Id,
    Guid OpportunityId,
    string Platform,
    string Title,
    string Niche,
    string? Category,
    string? Summary,
    decimal MarketOpportunityScore,
    decimal Confidence,
    decimal SellerFitScore,
    decimal FinalScore,
    int? Rank,
    string State,
    string? FitExplanation,
    IReadOnlyDictionary<string, double> FitBreakdown,
    DateTime LastEvaluatedAt,
    MarketOpportunityInsights Insights,
    OpportunityNicheMarket? NicheMarket = null);

public sealed record MarketOpportunityInsights(
    int EvidenceCount,
    int DistinctListingCount,
    int DistinctSellerCount,
    decimal? MedianPrice,
    decimal? MinimumPrice,
    decimal? MaximumPrice,
    double? EngagementRate,
    double? EngagementChangePercent,
    string EngagementTrend,
    string CompetitionLevel,
    string ConfidenceLabel,
    string EvidenceQuality,
    string PriceLabel,
    int ObservationPeriodCount,
    DateTime FirstObservedAt,
    DateTime LastObservedAt,
    string DataSource,
    int RealEvidenceCount,
    int SyntheticEvidenceCount,
    string FreshnessLabel,
    int? FreshnessAgeDays,
    int FreshnessScore,
    IReadOnlyDictionary<string, double> MarketScoreBreakdown,
    IReadOnlyList<MarketOpportunityEvidenceResponse> Evidence,
    double? FavoritesPerDay = null);

public sealed record MarketOpportunityEvidenceResponse(
    string Title,
    string? SourceUrl,
    decimal? Price,
    string? Currency,
    int? Favorites,
    int? Views,
    DateTime ObservedAt);

public sealed record SellerNicheMarketInsightResponse(
    string Niche,
    IReadOnlyList<string> MatchedQueries,
    string Verdict,
    string Conclusion,
    int CompetitorCount,
    string CompetitionLevel,
    string CompetitorTrend,
    double? CompetitorGrowthPercent,
    string ListingTrend,
    double? ListingGrowthPercent,
    string DemandTrend,
    string EvidenceQuality,
    int ObservationPeriodCount,
    double RelevanceScore,
    IReadOnlyList<SellerNicheCompetitorResponse> Competitors,
    IReadOnlyList<SellerNicheKeywordInsightResponse> Keywords,
    string DemandProxy,
    MarketSupplySignal MarketSupply,
    MarketListingSignals Signals,
    string Recommendation,
    IReadOnlyList<string> ActionPlan,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Highlights);

/// <summary>
/// The market signals of the niche (collected query) an opportunity belongs to, so an opportunity
/// card shows the same competition and demand as the niche view instead of its own few listings.
/// </summary>
public sealed record OpportunityNicheMarket(
    string Keyword,
    int? ActiveListingCount,
    double? SizePercentile,
    string CompetitionLevel,
    string DemandProxy,
    double? MedianFavoritesPer30Days,
    int SellerCount,
    int StrongSellerCount,
    int RisingNewListingCount,
    DateOnly? LatestObservedOn);

public sealed record SellerNicheCompetitorResponse(
    string SellerId,
    int ListingCount,
    string ListingTrend,
    double? ListingGrowthPercent,
    string RepresentativeListingTitle,
    string? RepresentativeListingUrl,
    decimal? RepresentativePrice);

public sealed record SellerNicheKeywordInsightResponse(
    string Keyword,
    int DemandProxyValue,
    int CompetitorCount,
    string CompetitionLevel,
    int KeywordScore,
    int ProfileMatchScore,
    IReadOnlyList<string> MatchedProfileTerms,
    double? MarketTrendScore,
    string Trend,
    double? TrendChangePercent,
    string EvidenceQuality,
    string DemandProxy,
    int? ActiveListingCount,
    MarketListingSignals Signals,
    double? SizePercentile);

internal sealed record SellerMarketInsightResponse(
    string Query,
    int DistinctListingCount,
    int DistinctSellerCount,
    decimal? MedianPrice,
    double? AverageFavorites,
    double? AverageViews,
    int NewListingCount,
    int ObservationPeriodCount,
    DateTime? FirstObservedAt,
    DateTime? LastObservedAt,
    string ListingTrend,
    double? ListingGrowthPercent,
    string EngagementTrend,
    double? EngagementChangePercent,
    string DemandProxy,
    string CompetitionLevel,
    string EvidenceQuality,
    string Seasonality,
    IReadOnlyList<MarketTagMetric> TopTags,
    double RelevanceScore,
    string InsightType,
    string Recommendation,
    IReadOnlyList<string> ActionPlan,
    IReadOnlyList<string> Risks,
    int? ActiveListingCount,
    MarketListingSignals Signals);

public sealed record UpdateSellerOpportunityStateRequest(string State);

public interface IOpportunityService
{
    Task<SellerResearchProfileResponse> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<SellerResearchProfileResponse> SaveProfileAsync(SellerResearchProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SellerNicheMarketInsightResponse>> GetMarketInsightsAsync(int limit = 12, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SellerOpportunityResponse>> RefreshRankingAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-ranks the radar of every seller who has one, after a day's market collection; returns how many.</summary>
    Task<int> RefreshAllRankingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SellerOpportunityResponse>> ListAsync(string? state = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<SellerOpportunityResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SellerOpportunityResponse?> UpdateStateAsync(Guid id, string state, CancellationToken cancellationToken = default);
}
