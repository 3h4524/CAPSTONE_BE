using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/design-images/{designImageId:guid}/mockups")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class MockupImagesController(IMockupTemplateService service) : ControllerBase
{
    /// <summary>SRS 3.5.9 execution: composites a generated design onto a mock-up template's base photo.</summary>
    [HttpPost]
    public async Task<IActionResult> Generate(
        Guid designImageId,
        GenerateMockupImageRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.GenerateCompositeAsync(designImageId, request, cancellationToken)).ToActionResult(this);
}
