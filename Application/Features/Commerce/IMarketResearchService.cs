using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

public interface IMarketResearchService
{
    Task<MarketResearchResponse> RetrieveAndIngestAsync(
        MarketResearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the queries, in their given order, that have no marketplace observation for
    /// <paramref name="observedOn"/> yet, so a schedule collects each query once per UTC day.
    /// </summary>
    Task<IReadOnlyList<string>> SelectUnobservedQueriesAsync(
        CommercePlatform platform,
        IReadOnlyList<string> queries,
        DateOnly observedOn,
        CancellationToken cancellationToken = default);
}
