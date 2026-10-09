using APCS.Api.Extensions;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class BatchJobsController(IDesignGenerationService service) : ControllerBase
{
    /// <summary>SRS 3.4.10 / 3.5.12: job status, progress counters, per-product state and generated images.</summary>
    [HttpGet("api/batch-jobs/{batchJobId:guid}")]
    public async Task<IActionResult> Get(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.GetJobAsync(batchJobId, cancellationToken)).ToActionResult(this);

    /// <summary>Lists the jobs of a batch, newest first, so the client can find the latest job id.</summary>
    [HttpGet("api/batches/{batchId:guid}/jobs")]
    public async Task<IActionResult> ListForBatch(Guid batchId, CancellationToken cancellationToken) =>
        (await service.ListJobsForBatchAsync(batchId, cancellationToken)).ToActionResult(this);

    /// <summary>SRS 3.5.6 "Generate Design Image": starts AI image generation for a draft batch job.</summary>
    [HttpPost("api/batch-jobs/{batchJobId:guid}/start")]
    public async Task<IActionResult> Start(
        Guid batchJobId,
        StartGenerationRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.StartAsync(batchJobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>SRS 3.5.12: approves or rejects the generated designs of a finished job (all pending ones when no ids are given).</summary>
    [HttpPut("api/batch-jobs/{batchJobId:guid}/design-images/approval")]
    public async Task<IActionResult> SetImageApproval(
        Guid batchJobId,
        SetImageApprovalRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.SetImageApprovalAsync(batchJobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Stops a queued or running job; products not generated yet are failed as cancelled.</summary>
    [HttpPost("api/batch-jobs/{batchJobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.CancelAsync(batchJobId, cancellationToken)).ToActionResult(this);

    /// <summary>Sets failed products, or ones whose designs were all rejected, back to pending for the next job.</summary>
    [HttpPost("api/batches/{batchId:guid}/products/reset")]
    public async Task<IActionResult> ResetProducts(
        Guid batchId,
        ResetProductsRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.ResetProductsAsync(batchId, request, cancellationToken)).ToActionResult(this);

    /// <summary>SRS 3.4.10 "Retry failed products": re-queues only the failed products of a finished job.</summary>
    [HttpPost("api/batch-jobs/{batchJobId:guid}/retry-failed")]
    public async Task<IActionResult> RetryFailed(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.RetryFailedAsync(batchJobId, cancellationToken)).ToActionResult(this);
}
