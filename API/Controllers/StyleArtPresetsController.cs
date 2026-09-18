using APCS.Api.Extensions;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/style-art-presets")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class StyleArtPresetsController(IStyleArtPresetService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StyleArtPresetResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        (await service.ListActiveAsync(cancellationToken)).ToActionResult(this);
}
