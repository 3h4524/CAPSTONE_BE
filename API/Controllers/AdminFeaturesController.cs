using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for the catalog of plan features an Admin puts on plans.
/// </summary>
[ApiController]
[Route("api/admin/features")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminFeaturesController(IFeatureCatalogAdminService catalogService) : ControllerBase
{
    /// <summary>
    /// Gets every plan feature, in display order, with the plans that include it.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminFeatureDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await catalogService.GetAllAsync(cancellationToken);

        return result.ToActionResult(this);
    }

    /// <summary>
    /// Changes how a plan feature is shown: label, description, group, order, whether it is
    /// offered when adding features to a plan, and whether the free trial includes it.
    /// </summary>
    [HttpPut("{code}")]
    [ProducesResponseType(typeof(AdminFeatureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string code, [FromBody] UpdateFeatureRequestDto request, CancellationToken cancellationToken)
    {
        var result = await catalogService.UpdateAsync(code, request, cancellationToken);

        return result.ToActionResult(this);
    }
}
