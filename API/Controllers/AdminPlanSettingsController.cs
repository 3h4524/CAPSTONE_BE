using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for what Free and the free trial include: their allowances,
/// and the plan the trial gives.
/// </summary>
[ApiController]
[Route("api/admin/plan-settings")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminPlanSettingsController(IPlanSettingsAdminService settingsService) : ControllerBase
{
    /// <summary>
    /// Gets Free's and the trial's allowances, the plan the trial gives, and who last changed them.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PlanSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await settingsService.GetAsync(cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Saves Free's and the trial's settings. They apply at once.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(PlanSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await settingsService.UpdateAsync(request, cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Marks a plan on sale as Recommended on Sellers' billing page, or none. Applies at once.
    /// </summary>
    [HttpPut("recommended-plan")]
    [ProducesResponseType(typeof(PlanSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetRecommendedPlan([FromBody] SetRecommendedPlanRequestDto request, CancellationToken cancellationToken)
    {
        var result = await settingsService.SetRecommendedPlanAsync(request, cancellationToken);

        return result.ToActionResult(this);
    }
}
