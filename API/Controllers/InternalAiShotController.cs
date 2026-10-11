using APCS.Api.Extensions;
using APCS.Api.Filters;
using APCS.Application.Features.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[TypeFilter(typeof(MediaWorkerAuthorizationFilter))]
[Route("internal/media-jobs/{jobId:guid}/ai-shots")]
public sealed class InternalAiShotController(IAiShotJobService service) : ControllerBase
{
    [HttpPost("{sceneOrder:int}/start")]
    public async Task<IActionResult> Start(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct) =>
        (await service.StartAsync(jobId, sceneOrder, request, ct)).ToActionResult(this);

    [HttpPost("{sceneOrder:int}/poll")]
    public async Task<IActionResult> Poll(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct) =>
        (await service.PollAsync(jobId, sceneOrder, request, ct)).ToActionResult(this);

    [HttpPost("{sceneOrder:int}/download")]
    public async Task<IActionResult> Download(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct)
    {
        var result = await service.DownloadAsync(jobId, sceneOrder, request, ct);
        return result.IsSuccess ? File(result.Value, "video/mp4") : result.ToActionResult(this);
    }

    [HttpPost("{sceneOrder:int}/stored")]
    public async Task<IActionResult> Stored(Guid jobId, int sceneOrder, AiShotStoredRequest request, CancellationToken ct) =>
        (await service.StoredAsync(jobId, sceneOrder, request, ct)).ToActionResult(this);
}
