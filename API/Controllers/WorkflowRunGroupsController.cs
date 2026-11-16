using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Batch video runs: one video per design confirmed at Mockup Approval.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class WorkflowRunGroupsController(IWorkflowRunGroupService groups, IRunCostEstimateService estimates,
    IRunGroupExportService exports) : ControllerBase
{
    /// <summary>How many videos of the run have a package ready to download together.</summary>
    [HttpGet("api/run-groups/{id:guid}/export")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct) => (await exports.SummaryAsync(id, ct)).ToActionResult(this);

    /// <summary>The link of the one ZIP that holds every ready package of the run, as a single video's download returns.</summary>
    [HttpGet("api/run-groups/{id:guid}/export/download")]
    public async Task<IActionResult> ExportDownload(Guid id, CancellationToken ct)
    {
        var summary = await exports.SummaryAsync(id, ct);
        if (summary.IsFailure) return summary.ToActionResult(this);
        return summary.Value.Ready > 0
            ? Ok(Url.ActionLink(nameof(ExportFile), values: new { id }))
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "No video package of this run is ready yet.");
    }

    [HttpGet("api/run-groups/{id:guid}/export/file")]
    public async Task<IActionResult> ExportFile(Guid id, CancellationToken ct)
    {
        var result = await exports.OpenAsync(id, ct);
        return result.IsSuccess ? File(result.Value, "application/zip", $"video-packages-{id:N}.zip") : result.ToActionResult(this);
    }

    /// <summary>What a Run of the saved workflow is expected to cost, item by item, shown before it starts.</summary>
    [HttpGet("api/workflows/{workflowId:guid}/run-estimate")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Estimate(Guid workflowId, CancellationToken ct) =>
        (await estimates.EstimateAsync(workflowId, ct)).ToActionResult(this);

    [HttpPost("api/workflows/{workflowId:guid}/run-groups")]
    public async Task<IActionResult> Start(Guid workflowId, StartRunGroupRequest request, CancellationToken ct)
    {
        var result = await groups.StartAsync(workflowId, request, ct);
        return result.IsSuccess ? Accepted($"/api/run-groups/{result.Value.Id}", result.Value) : result.ToActionResult(this);
    }

    [HttpGet("api/workflows/{workflowId:guid}/run-groups")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(Guid workflowId, CancellationToken ct) => (await groups.ListAsync(workflowId, ct)).ToActionResult(this);

    [HttpGet("api/run-groups/{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => (await groups.GetAsync(id, ct)).ToActionResult(this);

    [HttpPut("api/run-groups/{id:guid}/overrides")]
    public async Task<IActionResult> Override(Guid id, UpdateTargetOverrideRequest request, CancellationToken ct) =>
        (await groups.UpdateOverrideAsync(id, request, ct)).ToActionResult(this);

    /// <summary>One design's own Generate Video settings before its video starts; a null config shares the workflow's.</summary>
    [HttpPut("api/run-groups/{id:guid}/design-config")]
    public async Task<IActionResult> DesignConfig(Guid id, UpdateTargetConfigRequest request, CancellationToken ct) =>
        (await groups.UpdateConfigAsync(id, request, ct)).ToActionResult(this);

    /// <summary>What the AI clips of every design cost together, to show before the videos start.</summary>
    [HttpPost("api/run-groups/{id:guid}/ai-quote")]
    public async Task<IActionResult> AiQuote(Guid id, RunGroupRevisionRequest request, CancellationToken ct) =>
        (await groups.QuoteAiAsync(id, request, ct)).ToActionResult(this);

    [HttpPost("api/run-groups/{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, RunGroupRevisionRequest request, CancellationToken ct) =>
        (await groups.ConfirmAsync(id, request, ct)).ToActionResult(this);

    [HttpPost("api/run-groups/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, RunGroupRevisionRequest request, CancellationToken ct) =>
        (await groups.CancelAsync(id, request, ct)).ToActionResult(this);

    /// <summary>Goes on with a stopped run from where each design stopped, keeping what was already made.</summary>
    [HttpPost("api/run-groups/{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, RunGroupRevisionRequest request, CancellationToken ct) =>
        (await groups.ResumeAsync(id, request, ct)).ToActionResult(this);
}
