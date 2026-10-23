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
    DateTime LastEvaluatedAt);

public sealed record UpdateSellerOpportunityStateRequest(string State);

public interface IOpportunityService
{
    Task<SellerResearchProfileResponse> GetProfileAsync(CancellationToken cancellationToken = default);
    Task<SellerResearchProfileResponse> SaveProfileAsync(SellerResearchProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SellerOpportunityResponse>> RefreshRankingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SellerOpportunityResponse>> ListAsync(string? state = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<SellerOpportunityResponse?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SellerOpportunityResponse?> UpdateStateAsync(Guid id, string state, CancellationToken cancellationToken = default);
}
