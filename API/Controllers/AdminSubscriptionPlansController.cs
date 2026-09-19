using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for managing subscription plan tiers (ADM-03/04/05/20,
/// 3.6.10 Delete Subscription Plan).
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminSubscriptionPlansController(IAdminSubscriptionPlanService planService) : ControllerBase
{
    /// <summary>
    /// Gets every plan tier for the Subscription Plans list (ADM-03).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminPlanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await planService.GetAllAsync(cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Gets a single plan tier's details (ADM-04).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await planService.GetByIdAsync(id, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Creates a new plan tier (ADM-05 Add Subscription Plan).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreatePlanRequestDto request, CancellationToken cancellationToken)
    {
        var result = await planService.CreateAsync(request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Updates an existing plan tier's pricing, limits, features, and active status
    /// (3.6.9 Edit Subscription Plan).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePlanRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await planService.UpdateAsync(id, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }

    /// <summary>
    /// Permanently deletes a plan tier (3.6.10 Delete Subscription Plan). Fails with 409 when the
    /// plan has any subscription history — deactivate it instead via <see cref="Update"/>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        DeletePlanRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await planService.DeleteAsync(id, request, cancellationToken);

        return result.ToActionResult(this);
    }
}
