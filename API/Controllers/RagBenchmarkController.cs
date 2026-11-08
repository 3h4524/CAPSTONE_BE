using APCS.Application.Features.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.AdminRole)]
[Route("api/rag/admin/benchmark")]
public sealed class RagBenchmarkController(IRagBenchmarkService service) : ControllerBase
{
    /// <summary>
    /// Samples a new known-item case set, or runs a saved one again when caseSetId is given, and scores
    /// the retrieval (hit@k, mean reciprocal rank and the rank of each case's expected listing).
    /// </summary>
    [HttpPost("known-item")]
    public async Task<IActionResult> RunKnownItem(
        RagKnownItemBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.RunKnownItemAsync(request, cancellationToken);
            return result is null
                ? NotFound(new { message = "The case set was not found or has expired. Create a new one." })
                : Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
