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

    /// <summary>SRS 3.4.10 "Retry failed products": re-queues only the failed products of a finished job.</summary>
    [HttpPost("api/batch-jobs/{batchJobId:guid}/retry-failed")]
    public async Task<IActionResult> RetryFailed(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.RetryFailedAsync(batchJobId, cancellationToken)).ToActionResult(this);
}
