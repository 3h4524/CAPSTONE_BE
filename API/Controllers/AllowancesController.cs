using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Entitlements;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>What a Seller on Free or the Pro trial has used of their allowances.</summary>
[ApiController]
[Route("api/allowances")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class AllowancesController(IPlanEntitlementService entitlements, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>The allowances and what is used of them; no items on a paid plan.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(UsageAllowanceSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId) return Unauthorized();

        return Ok(await entitlements.GetAllowancesAsync(userId, cancellationToken));
    }
}
