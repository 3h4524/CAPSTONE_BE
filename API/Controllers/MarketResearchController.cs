using APCS.Application.Features.Commerce;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.AdminRole)]
[Route("api/market-research")]
public sealed class MarketResearchController(IMarketResearchService service, IMarketResearchScheduleService scheduleService) : ControllerBase
{
    /// <summary>Retrieves public marketplace listings and queues selected references for RAG ingestion.</summary>
    [HttpPost("retrieve-and-ingest")]
    public async Task<IActionResult> RetrieveAndIngest(
        MarketResearchRequest request,
        CancellationToken cancellationToken) =>
        Accepted(await service.RetrieveAndIngestAsync(request, cancellationToken));

    [HttpGet("schedule")]
    public async Task<MarketResearchSchedule> GetSchedule(CancellationToken cancellationToken) => await scheduleService.GetAsync(cancellationToken);

    [HttpPut("schedule")]
    public async Task<MarketResearchSchedule> UpdateSchedule(UpdateMarketResearchScheduleRequest request, CancellationToken cancellationToken) => await scheduleService.UpdateAsync(request, cancellationToken);
}
