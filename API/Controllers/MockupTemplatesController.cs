using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/mockup-templates")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class MockupTemplatesController(IMockupTemplateService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MockupTemplateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? productType, CancellationToken cancellationToken) =>
        (await service.ListAsync(productType, cancellationToken)).ToActionResult(this);
}
