using APCS.Application.Features.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/market-opportunities")]
public sealed class MarketOpportunitiesController(
    IOpportunityService service,
    IOpportunityAnalysisService analysisService) : ControllerBase
{
    [HttpGet("profile")]
    public Task<SellerResearchProfileResponse> GetProfile(CancellationToken cancellationToken) =>
        service.GetProfileAsync(cancellationToken);

    [HttpPut("profile")]
    public Task<SellerResearchProfileResponse> SaveProfile(
        SellerResearchProfileRequest request,
        CancellationToken cancellationToken) =>
        service.SaveProfileAsync(request, cancellationToken);

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken) =>
        Ok(await service.RefreshRankingAsync(cancellationToken));

    [HttpGet]
    public Task<IReadOnlyList<SellerOpportunityResponse>> List(
        [FromQuery] string? state,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(state, limit, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPatch("{id:guid}/state")]
    public async Task<IActionResult> UpdateState(
        Guid id,
        UpdateSellerOpportunityStateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateStateAsync(id, request.State, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid id, CancellationToken cancellationToken)
    {
        var result = await analysisService.AnalyzeAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
