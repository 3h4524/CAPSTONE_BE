namespace APCS.Application.Features.Commerce;

public interface IMarketResearchScheduleService
{
    Task<MarketResearchSchedule> GetAsync(CancellationToken cancellationToken = default);
    Task<MarketResearchSchedule> UpdateAsync(UpdateMarketResearchScheduleRequest request, CancellationToken cancellationToken = default);
    Task MarkRunAsync(DateTimeOffset runAtUtc, int processedQueryCount, CancellationToken cancellationToken = default);
    Task DisableAsync(CancellationToken cancellationToken = default);
}
