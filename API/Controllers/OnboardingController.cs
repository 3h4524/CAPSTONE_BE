using APCS.Api.Extensions;
using APCS.Application.Features.Onboarding;
using APCS.Application.Features.Onboarding.Dtos.Request;
using APCS.Application.Features.Onboarding.Dtos.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// The authenticated user's onboarding state (dashboard checklist, seen page guides).
/// The table is added by a Flyway migration; reads and writes go through plain SQL and tolerate the table not existing yet.
/// </summary>
[ApiController]
[Authorize]
[Route("api/profile/onboarding")]
public sealed class OnboardingController(IOnboardingProgressService onboarding) : ControllerBase
{
    /// <summary>
    /// Gets the authenticated user's onboarding state.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(OnboardingProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await onboarding.GetAsync(cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Replaces the authenticated user's onboarding state.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(OnboardingProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Put(
        UpdateOnboardingProgressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await onboarding.UpdateAsync(request, cancellationToken);
        return result.ToActionResult(this);
    }
}
