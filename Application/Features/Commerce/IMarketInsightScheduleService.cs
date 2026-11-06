namespace APCS.Application.Features.Commerce;

public interface IMarketInsightScheduleService
{
    Task<MarketResearchSchedule> GetForCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<MarketResearchSchedule> UpdateForCurrentUserAsync(
        UpdateMarketInsightScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketResearchSchedule> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetScheduledUserIdsAsync(CancellationToken cancellationToken = default);

    Task MarkRunAsync(Guid userId, DateTimeOffset runAtUtc, Guid jobId, CancellationToken cancellationToken = default);
}

public sealed record UpdateMarketInsightScheduleRequest(
    bool Enabled,
    string Query,
    int IntervalMinutes);
