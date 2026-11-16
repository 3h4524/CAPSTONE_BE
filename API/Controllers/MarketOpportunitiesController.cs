using APCS.Api.Extensions;
using APCS.Application.Features.Commerce;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/market-opportunities")]
public sealed class MarketOpportunitiesController(
    IOpportunityService service,
    IOpportunityAnalysisService analysisService,
    IMarketRadarAccess access) : ControllerBase
{
    [HttpGet("profile")]
    public Task<SellerResearchProfileResponse> GetProfile(CancellationToken cancellationToken) =>
        service.GetProfileAsync(cancellationToken);

    [HttpPut("profile")]
    public Task<SellerResearchProfileResponse> SaveProfile(
        SellerResearchProfileRequest request,
        CancellationToken cancellationToken) =>
        service.SaveProfileAsync(request, cancellationToken);

    /// <summary>What the Seller's plan opens of the radar, so the page can show what is locked.</summary>
    [HttpGet("access")]
    public Task<MarketRadarAccessResponse> GetAccess(CancellationToken cancellationToken) =>
        service.GetAccessAsync(cancellationToken);

    /// <summary>Ranks the radar again now; without the whole radar at most once a day.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var allowed = await access.RequireRefreshAsync(cancellationToken);
        if (allowed.IsFailure) return allowed.ToActionResult(this);
        return Ok(await service.RefreshRankingAsync(cancellationToken));
    }

    [HttpGet("insights")]
    public Task<IReadOnlyList<SellerNicheMarketInsightResponse>> GetInsights(
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default) =>
        service.GetMarketInsightsAsync(limit, cancellationToken);

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
    public async Task<IActionResult> Analyze(Guid id, CancellationToken cancellationToken) =>
        (await analysisService.AnalyzeAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/analysis/latest")]
    public async Task<IActionResult> GetLatestAnalysis(Guid id, CancellationToken cancellationToken)
    {
        var allowed = await access.RequireFullAsync(cancellationToken);
        if (allowed.IsFailure) return allowed.ToActionResult(this);
        var result = await analysisService.GetLatestAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
