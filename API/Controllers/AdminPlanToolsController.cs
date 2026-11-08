using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Common.Constants;
using APCS.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints to duplicate a plan, put the plans in order, and list the
/// history of plan changes.
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminPlanToolsController(IPlanToolsService toolsService) : ControllerBase
{
    /// <summary>
    /// Copies a plan, its prices, limits and features, under a new name and tier. The copy is not on sale.
    /// </summary>
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType(typeof(DuplicatedPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DuplicatePlanRequestDto request, CancellationToken cancellationToken)
    {
        var result = await toolsService.DuplicateAsync(id, request, cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Puts the plans in the order given, for the Admin and for Sellers comparing plans.
    /// </summary>
    [HttpPut("order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder([FromBody] ReorderPlansRequestDto request, CancellationToken cancellationToken)
    {
        var result = await toolsService.ReorderAsync(request, cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Lists one page of the changes to the plans, their features, the feature catalog, Free and
    /// the trial, newest first. pageSize is at most 100.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(PagedResult<PlanHistoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default, [FromQuery] string? action = null)
    {
        var result = await toolsService.GetHistoryAsync(pageNumber, pageSize, cancellationToken, action);

        return result.ToActionResult(this);
    }
}
