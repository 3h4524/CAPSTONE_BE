using APCS.Api.Extensions;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/products/{productId:guid}/listing-content")]
public sealed class ListingContentController(IListingContentService service) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        Guid productId,
        [FromBody] ListingSeoFeedbackDto? seoFeedback,
        CancellationToken cancellationToken) =>
        (await service.GenerateAsync(productId, cancellationToken, seoFeedback)).ToActionResult(this);

    [HttpGet]
    public async Task<IActionResult> Get(Guid productId, CancellationToken cancellationToken) =>
        (await service.GetAsync(productId, cancellationToken)).ToActionResult(this);

    [HttpPost("seo-score")]
    public async Task<IActionResult> EvaluateSeo(Guid productId, CancellationToken cancellationToken) =>
        (await service.EvaluateSeoAsync(productId, cancellationToken)).ToActionResult(this);
}
