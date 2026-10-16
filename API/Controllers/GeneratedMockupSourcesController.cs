using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Makes the mock-ups composited for a product available as sources of its video.
/// </summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/products/{productId:guid}/mockup-assets")]
public sealed class GeneratedMockupSourcesController(IGeneratedMockupSourceService generatedMockups) : ControllerBase
{
    [HttpPost("import-generated")]
    public async Task<IActionResult> Import(Guid productId, CancellationToken ct) =>
        (await generatedMockups.ImportAsync(productId, ct)).ToActionResult(this);
}
