using APCS.Api.Extensions;
using APCS.Api.Filters;
using APCS.Application.Features.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace APCS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[TypeFilter(typeof(MediaWorkerAuthorizationFilter))]
[DisableRateLimiting]
[Route("internal/media-jobs")]
public sealed class InternalMediaJobsController(IMediaJobService service, IConfiguration? configuration = null) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(CancellationToken ct)
    {
        var mode = configuration?["MediaQueue:Mode"] ?? "polling";
        if (!string.Equals(mode, "polling", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { code = "MediaPollingDisabled" });
        var result = await service.ClaimAsync(ct);
        return result.IsSuccess && result.Value == null ? NoContent() : result.ToActionResult(this);
    }
    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> ClaimStream(Guid id, MediaStreamClaimRequest request, CancellationToken ct) =>
        (await service.ClaimStreamAsync(id, request.QueueVersion, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid id, JobHeartbeat request, CancellationToken ct) => (await service.HeartbeatAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/package")]
    [Consumes("application/zip", "application/octet-stream")]
    [RequestSizeLimit(400L * 1024 * 1024)]
    public async Task<IActionResult> UploadPackage(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(Request.Headers["X-Media-Job-Lease"], out var leaseToken)) return BadRequest();
        var result = await service.UploadExportAsync(id, leaseToken, Request.Body, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult(this);
    }
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, JobCompletion request, CancellationToken ct) => (await service.CompleteAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/fail")]
    public async Task<IActionResult> Fail(Guid id, JobFailure request, CancellationToken ct) => (await service.FailAsync(id, request, ct)).ToActionResult(this);
}
