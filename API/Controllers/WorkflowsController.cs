using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/workflows")]
public sealed class WorkflowsController(IWorkflowService workflows, IWorkflowRunService runs, WorkflowCapabilityRegistry capabilities) : ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(capabilities.Get());
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => (await workflows.ListAsync(ct)).ToActionResult(this);
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await workflows.GetAsync(id, ct)).ToActionResult(this);
    [HttpPost]
    public async Task<IActionResult> Create(SaveWorkflowRequest request, CancellationToken ct)
    {
        var result = await workflows.SaveAsync(null, request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value) : result.ToActionResult(this);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Save(Guid id, SaveWorkflowRequest request, CancellationToken ct) => (await workflows.SaveAsync(id, request, ct)).ToActionResult(this);
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] long revision, CancellationToken ct) => (await workflows.DeleteAsync(id, revision, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/runs")]
    public async Task<IActionResult> Run(Guid id, StartRunRequest request, CancellationToken ct)
    {
        var result = await runs.StartAsync(id, request, ct);
        return result.IsSuccess ? Accepted($"/api/workflow-runs/{result.Value.Id}", result.Value) : result.ToActionResult(this);
    }
    /// <summary>Returns the most recent run for a workflow so its generated media can be restored.</summary>
    [HttpGet("{id:guid}/runs/latest")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(RunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LatestRun(Guid id, CancellationToken ct) =>
        (await runs.GetLatestAsync(id, ct)).ToActionResult(this);
    /// <summary>Lists previous runs that contain generated videos available for review or download.</summary>
    [HttpGet("{id:guid}/video-runs")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(VideoRunSummaryResponse[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> VideoRuns(Guid id, CancellationToken ct) =>
        (await runs.ListVideoRunsAsync(id, ct)).ToActionResult(this);
}
