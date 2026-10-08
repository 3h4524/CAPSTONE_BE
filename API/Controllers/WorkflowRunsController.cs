using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/workflow-runs")]
public sealed class WorkflowRunsController(IWorkflowRunService runs, IVideoArtifactService media) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await runs.GetAsync(id, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/actions")]
    public async Task<IActionResult> Action(Guid id, RunActionRequest request, CancellationToken ct) => (await runs.ActionAsync(id, request, ct)).ToActionResult(this);
    [HttpGet("{id:guid}/videos")]
    public async Task<IActionResult> Videos(Guid id, CancellationToken ct) => (await media.ListAsync(id, ct)).ToActionResult(this);
}
