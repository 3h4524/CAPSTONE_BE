using APCS.Api.Extensions;
using APCS.Application.Features.Profile;
using APCS.Application.Features.Profile.Dtos.Request;
using APCS.Application.Features.Profile.Dtos.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides the authenticated user's profile endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(IProfileService profileService) : ControllerBase
{
    /// <summary>
    /// Gets the authenticated user's profile.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await profileService.GetProfileAsync(cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Partially updates the authenticated user's profile.
    /// </summary>
    [HttpPatch]
    [ProducesResponseType(typeof(ProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Patch(
        UpdateProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await profileService.UpdateProfileAsync(request, cancellationToken);
        return result.ToActionResult(this);
    }
}
