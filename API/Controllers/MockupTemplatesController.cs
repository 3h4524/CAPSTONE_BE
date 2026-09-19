using APCS.Api.Contracts.MockupTemplates;
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

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] CreateMockupTemplateForm form,
        CancellationToken cancellationToken)
    {
        var baseImage = form.BaseImage.ToUploadFileDto();
        var preview = form.Preview.ToUploadFileDto();
        try
        {
            var result = await service.CreateAsync(
                new CreateMockupTemplateRequestDto(
                    form.Name,
                    form.ProductType,
                    form.PrintAreaConfig,
                    form.OutputWidthPx,
                    form.OutputHeightPx,
                    baseImage,
                    preview),
                cancellationToken);

            return result.IsSuccess
                ? CreatedAtAction(nameof(List), result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            baseImage?.Content.Dispose();
            preview?.Content.Dispose();
        }
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] UpdateMockupTemplateForm form,
        CancellationToken cancellationToken)
    {
        var baseImage = form.BaseImage.ToUploadFileDto();
        var preview = form.Preview.ToUploadFileDto();
        try
        {
            var result = await service.UpdateAsync(
                id,
                new UpdateMockupTemplateRequestDto(
                    form.Name,
                    form.ProductType,
                    form.PrintAreaConfig,
                    form.OutputWidthPx,
                    form.OutputHeightPx,
                    baseImage,
                    preview,
                    form.DeletePreview),
                cancellationToken);

            return result.ToActionResult(this);
        }
        finally
        {
            baseImage?.Content.Dispose();
            preview?.Content.Dispose();
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToActionResult(this);
}
