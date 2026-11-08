using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Puts a product's mock-up on a model photo, as a Lifestyle image for its videos.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/products/{productId:guid}/mockup-assets/on-model")]
public sealed class ModelPhotoMockupsController(IModelPhotoMockupService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(MockupResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct)
    {
        var result = await service.GenerateAsync(productId, request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    /// <summary>How it would look, as an image URL; nothing is stored.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ModelPhotoPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct) =>
        (await service.PreviewAsync(productId, request, ct)).ToActionResult(this);
}
