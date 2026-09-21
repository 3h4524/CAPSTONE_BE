using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/batch-jobs/{batchJobId:guid}/mockups")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class BatchMockupsController(IMockupTemplateService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BatchMockupSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.GetSelectionAsync(batchJobId, cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(typeof(BatchMockupSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply(
        Guid batchJobId,
        [FromBody] ApplyMockupTemplatesRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.ApplyAsync(batchJobId, request, cancellationToken)).ToActionResult(this);
}
