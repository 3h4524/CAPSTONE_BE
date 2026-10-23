using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

public interface IMarketResearchService
{
    Task<MarketResearchResponse> RetrieveAndIngestAsync(
        MarketResearchRequest request,
        CancellationToken cancellationToken = default);
}
