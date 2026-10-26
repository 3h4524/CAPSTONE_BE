using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

public sealed record MarketResearchRequest(
    CommercePlatform Platform,
    IReadOnlyCollection<string> Queries,
    int LimitPerQuery = 25,
    double MinimumViralScore = 0,
    int TopN = 3);

public sealed record MarketResearchItem(
    string ExternalListingId,
    string Title,
    double ViralScore,
    int? Favorites,
    int? Views,
    Guid RagDocumentId,
    string? Url,
    string? MatchedQuery,
    MarketResearchScoreBreakdown ScoreBreakdown);

public sealed record MarketResearchScoreBreakdown(
    double? FavoritesScore,
    double? ViewsScore,
    double FavoritesWeight,
    double ViewsWeight);

public sealed record MarketResearchResponse(
    CommercePlatform Platform,
    int RetrievedCount,
    int SelectedCount,
    IReadOnlyList<MarketResearchItem> Items);

public sealed record MarketResearchSchedule(
    bool Enabled,
    CommercePlatform Platform,
    IReadOnlyList<string> Queries,
    int LimitPerQuery,
    double MinimumViralScore,
    int TopN,
    int IntervalMinutes,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc);

public sealed record UpdateMarketResearchScheduleRequest(
    bool Enabled,
    CommercePlatform Platform,
    IReadOnlyCollection<string> Queries,
    int LimitPerQuery,
    double MinimumViralScore,
    int TopN,
    int IntervalMinutes);
