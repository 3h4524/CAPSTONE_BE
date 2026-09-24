using APCS.Api.Extensions;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/batch-jobs/{batchJobId:guid}")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class BatchJobsController(IDesignGenerationService service) : ControllerBase
{
    /// <summary>SRS 3.5.6 "Generate Design Image" — starts AI image generation for a draft batch job.</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start(
        Guid batchJobId,
        StartGenerationRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.StartAsync(batchJobId, request, cancellationToken)).ToActionResult(this);
}
