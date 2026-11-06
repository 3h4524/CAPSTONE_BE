using APCS.Api.Extensions;
using APCS.Application.Features.SellerDashboard;
using APCS.Application.Features.SellerDashboard.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides endpoints for the Seller's Dashboard.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class SellerDashboardController(ISellerDashboardService service) : ControllerBase
{
    /// <summary>
    /// Gets the Seller's stage, the period's figures, the pipeline and the batch jobs still running.
    /// </summary>
    /// <param name="days">The period: 7, 30 or 90 days.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSummaryAsync([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        (await service.GetSummaryAsync(days, cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Gets the work that waits on the Seller, the most pressing first.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    [HttpGet("action-items")]
    [ProducesResponseType(typeof(DashboardActionItemsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetActionItemsAsync(CancellationToken cancellationToken = default) =>
        (await service.GetActionItemsAsync(cancellationToken)).ToActionResult(this);
}
