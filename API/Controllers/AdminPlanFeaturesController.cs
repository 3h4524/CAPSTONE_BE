using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for putting features on a plan and taking them off. Subscribers
/// already on the plan keep what a change takes away until their period ends.
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans/{planId:guid}/features")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminPlanFeaturesController(IPlanFeatureAssignmentService assignmentService) : ControllerBase
{
    /// <summary>
    /// Adds features to the plan; a limit gets the value sent.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlanFeatureChangesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid planId, [FromBody] AddPlanFeaturesRequestDto request, CancellationToken cancellationToken)
    {
        var result = await assignmentService.AddAsync(planId, request, cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sets a limit feature's value; null means no limit. Lowering it lets current subscribers keep
    /// the old value until their period ends.
    /// </summary>
    [HttpPatch("{code}")]
    [ProducesResponseType(typeof(PlanFeatureChangesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLimit(Guid planId, string code, [FromBody] UpdatePlanFeatureLimitRequestDto request, CancellationToken cancellationToken)
    {
        var result = await assignmentService.UpdateLimitAsync(planId, code, request.LimitValue, cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Takes an on/off feature off the plan. Current subscribers keep it until their period ends.
    /// </summary>
    [HttpDelete("{code}")]
    [ProducesResponseType(typeof(PlanFeatureChangesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid planId, string code, CancellationToken cancellationToken)
    {
        var result = await assignmentService.RemoveAsync(planId, code, cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Shows who a change would reach before it is made: without limitValue, removing an on/off
    /// feature; with it, setting a limit to that value (-1 for no limit).
    /// </summary>
    [HttpGet("{code}/impact")]
    [ProducesResponseType(typeof(PlanFeatureImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImpact(Guid planId, string code, [FromQuery] int? limitValue, CancellationToken cancellationToken)
    {
        var result = await assignmentService.GetImpactAsync(planId, code, limitValue, cancellationToken);

        return result.ToActionResult(this);
    }
}
