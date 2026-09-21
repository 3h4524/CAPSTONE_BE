using APCS.Api.Extensions;
using APCS.Application.Features.BatchProductPrompts;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using APCS.Application.Features.BatchProductPrompts.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/batch-job-products/{rowId:guid}/prompt")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class BatchProductPromptsController(IBatchProductPromptService service) : ControllerBase
{
    [HttpGet("default")]
    [ProducesResponseType(typeof(BatchProductPromptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDefault(Guid rowId, CancellationToken cancellationToken) =>
        (await service.GetDefaultAsync(rowId, cancellationToken)).ToActionResult(this);

    [HttpGet]
    [ProducesResponseType(typeof(BatchProductPromptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid rowId, CancellationToken cancellationToken) =>
        (await service.GetEffectiveAsync(rowId, cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(typeof(BatchProductPromptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(
        Guid rowId,
        [FromBody] UpdateBatchProductPromptRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.SaveAsync(rowId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete]
    [ProducesResponseType(typeof(BatchProductPromptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid rowId, CancellationToken cancellationToken) =>
        (await service.RestoreAsync(rowId, cancellationToken)).ToActionResult(this);
}
