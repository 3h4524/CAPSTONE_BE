using APCS.Api.Contracts.BatchMockups;
using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/mockup-templates")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class MockupTemplatesController(IMockupTemplateService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MockupTemplateResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? productType, CancellationToken cancellationToken) =>
        (await service.ListAsync(productType, cancellationToken)).ToActionResult(this);

    /// <summary>SRS 3.5.9 execution: lets a Seller upload a real base photo for a personal mock-up template.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromForm] CreateMockupTemplateForm form, CancellationToken cancellationToken)
    {
        var baseImage = form.BaseImage.ToUploadFileDto();
        try
        {
            var result = await service.CreateAsync(
                new CreateMockupTemplateRequestDto(form.Name, form.ProductType, form.X, form.Y, form.Width, form.Height, baseImage, form.AllowRecolor,
                    string.IsNullOrWhiteSpace(form.GarmentColor) ? null : form.GarmentColor),
                cancellationToken);
            return result.ToActionResult(this);
        }
        finally
        {
            baseImage?.Content.Dispose();
        }
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateMockupTemplateForm form, CancellationToken cancellationToken)
    {
        var baseImage = form.BaseImage.ToUploadFileDto();
        try
        {
            var result = await service.UpdateAsync(
                id,
                new UpdateMockupTemplateRequestDto(form.Name, form.ProductType, form.X, form.Y, form.Width, form.Height, baseImage, form.AllowRecolor,
                    string.IsNullOrWhiteSpace(form.GarmentColor) ? null : form.GarmentColor),
                cancellationToken);
            return result.ToActionResult(this);
        }
        finally
        {
            baseImage?.Content.Dispose();
        }
    }

    /// <summary>Previews recoloring for a base photo that is not saved yet.</summary>
    [HttpPost("garment-mask-preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GarmentMaskPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewGarmentMask(IFormFile? baseImage, CancellationToken cancellationToken)
    {
        var photo = baseImage.ToUploadFileDto();
        try
        {
            return (await service.PreviewGarmentMaskAsync(photo, cancellationToken)).ToActionResult(this);
        }
        finally
        {
            photo?.Content.Dispose();
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToActionResult(this);
}
