using APCS.Api.Extensions;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.AdminRole)]
[Route("api/rag/admin/listing-evaluation")]
public sealed class RagListingEvaluationController(IListingContentService service) : ControllerBase
{
    [HttpPost("retrieval")]
    public async Task<IActionResult> EvaluateRetrieval(
        ListingRetrievalEvaluationRequest request,
        CancellationToken cancellationToken) =>
        (await service.EvaluateRetrievalForAdminAsync(request.ProductId, cancellationToken)).ToActionResult(this);
}
