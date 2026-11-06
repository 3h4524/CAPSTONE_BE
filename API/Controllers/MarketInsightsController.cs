using APCS.Application.Features.Commerce;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.AdminRole)]
[Route("api/market-insights")]
public sealed class MarketInsightsController(
    IMarketInsightsService service,
    IMarketInsightScheduleService scheduleService) : ControllerBase
{
    [HttpGet("{keyword}")]
    public async Task<IActionResult> Get(
        string keyword,
        CancellationToken cancellationToken)
    {
        var result = await service.GetQueryMetricsAsync(keyword, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Collects a fresh public Etsy snapshot for one keyword.</summary>
    [HttpPost("collect")]
    public async Task<IActionResult> Collect(
        CollectMarketInsightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.QueueCollectionAsync(request.Query, cancellationToken);
        return Accepted(result);
    }

    [HttpGet("collect/{jobId:guid}")]
    public async Task<IActionResult> GetCollectionStatus(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCollectionStatusAsync(jobId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("schedule")]
    public Task<MarketResearchSchedule> GetSchedule(CancellationToken cancellationToken) =>
        scheduleService.GetForCurrentUserAsync(cancellationToken);

    [HttpPut("schedule")]
    public Task<MarketResearchSchedule> UpdateSchedule(
        UpdateMarketInsightScheduleRequest request,
        CancellationToken cancellationToken) =>
        scheduleService.UpdateForCurrentUserAsync(request, cancellationToken);
}

public sealed record CollectMarketInsightRequest(string Query);
