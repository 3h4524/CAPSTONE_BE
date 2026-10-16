namespace APCS.Application.Features.ListingContent.Dtos;

public sealed record ListingSeoFeedbackDto(
    decimal OverallScore,
    decimal TitleScore,
    decimal TagsScore,
    decimal DescriptionScore,
    decimal KeywordOptimizationScore,
    decimal TagRelevanceScore,
    decimal KeywordDensity,
    IReadOnlyList<string> ImprovementSuggestions,
    string Recommendation,
    bool ShouldRegenerate);
