using APCS.Api.Extensions;
using APCS.Api.Filters;
using APCS.Application.Features.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[TypeFilter(typeof(MediaWorkerAuthorizationFilter))]
[Route("internal/media-jobs")]
public sealed class InternalMediaJobsController(IMediaJobService service) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(CancellationToken ct)
    {
        var result = await service.ClaimAsync(ct);
        return result.IsSuccess && result.Value == null ? NoContent() : result.ToActionResult(this);
    }
    [HttpPost("{id:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid id, JobHeartbeat request, CancellationToken ct) => (await service.HeartbeatAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, JobCompletion request, CancellationToken ct) => (await service.CompleteAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("{id:guid}/fail")]
    public async Task<IActionResult> Fail(Guid id, JobFailure request, CancellationToken ct) => (await service.FailAsync(id, request, ct)).ToActionResult(this);
}
