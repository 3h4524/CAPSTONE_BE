using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = $"{AuthConstants.UserRole},{AuthConstants.AdminRole}")]
[Route("api/rag")]
public sealed class RagController(IRagService service) : ControllerBase
{
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest(RagIngestRequest request, CancellationToken cancellationToken)
    {
        var documentId = await service.IngestAsync(request, cancellationToken);
        return Ok(new { documentId });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(RagSearchRequest request, CancellationToken cancellationToken)
    {
        var results = await service.SearchAsync(request, cancellationToken);
        return Ok(results);
    }
}
