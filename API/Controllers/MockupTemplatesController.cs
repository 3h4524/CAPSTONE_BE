using APCS.Api.Contracts.BatchMockups;
using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
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
    public Task<IActionResult> Create([FromForm] CreateMockupTemplateForm form, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.CreateAsync(this, form, request => service.CreateAsync(request, system: false, cancellationToken));

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Update(Guid id, [FromForm] UpdateMockupTemplateForm form, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.UpdateAsync(this, form, request => service.UpdateAsync(id, request, system: false, cancellationToken));

    /// <summary>Previews recoloring for a base photo that is not saved yet.</summary>
    [HttpPost("garment-mask-preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(GarmentMaskPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> PreviewGarmentMask(IFormFile? baseImage, CancellationToken cancellationToken) =>
        MockupTemplateEndpoints.PreviewAsync(this, baseImage, photo => service.PreviewGarmentMaskAsync(photo, system: false, cancellationToken));

    /// <summary>Gives the template the photo of another side (back, left, right), a template of the Seller's own; null takes it off.</summary>
    [HttpPut("{id:guid}/views/{view}")]
    [ProducesResponseType(typeof(MockupTemplateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetView(Guid id, string view, [FromBody] SetMockupTemplateViewRequest request, CancellationToken cancellationToken) =>
        (await service.SetViewAsync(id, view, request.TemplateId, system: false, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, system: false, cancellationToken)).ToActionResult(this);
}

/// <summary>Reads the mock-up template forms, for the seller's and the admin's endpoints alike.</summary>
internal static class MockupTemplateEndpoints
{
    private static readonly System.Text.Json.JsonSerializerOptions AreasJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static async Task<IActionResult> CreateAsync(ControllerBase controller, CreateMockupTemplateForm form,
        Func<CreateMockupTemplateRequestDto, Task<Result<MockupTemplateResponseDto>>> create)
    {
        if (!TryReadAreas(form.Areas, out var areas))
            return AreasNotReadable(controller);
        var baseImage = form.BaseImage.ToUploadFileDto();
        try
        {
            var result = await create(new CreateMockupTemplateRequestDto(form.Name, form.ProductType, form.X, form.Y, form.Width, form.Height,
                baseImage, form.AllowRecolor,
                string.IsNullOrWhiteSpace(form.GarmentColor) ? null : form.GarmentColor,
                string.IsNullOrWhiteSpace(form.View) ? null : form.View, areas, form.MainTemplateId));
            return result.ToActionResult(controller);
        }
        finally
        {
            baseImage?.Content.Dispose();
        }
    }

    public static async Task<IActionResult> UpdateAsync(ControllerBase controller, UpdateMockupTemplateForm form,
        Func<UpdateMockupTemplateRequestDto, Task<Result<MockupTemplateResponseDto>>> update)
    {
        if (!TryReadAreas(form.Areas, out var areas))
            return AreasNotReadable(controller);
        var baseImage = form.BaseImage.ToUploadFileDto();
        try
        {
            var result = await update(new UpdateMockupTemplateRequestDto(form.Name, form.ProductType, form.X, form.Y, form.Width, form.Height,
                baseImage, form.AllowRecolor,
                string.IsNullOrWhiteSpace(form.GarmentColor) ? null : form.GarmentColor,
                string.IsNullOrWhiteSpace(form.View) ? null : form.View, areas));
            return result.ToActionResult(controller);
        }
        finally
        {
            baseImage?.Content.Dispose();
        }
    }

    public static async Task<IActionResult> PreviewAsync(ControllerBase controller, IFormFile? baseImage,
        Func<APCS.Application.Abstractions.Storage.UploadFileDto?, Task<Result<GarmentMaskPreviewResponseDto>>> preview)
    {
        var photo = baseImage.ToUploadFileDto();
        try
        {
            return (await preview(photo)).ToActionResult(controller);
        }
        finally
        {
            photo?.Content.Dispose();
        }
    }

    // The print areas sent as one JSON form field; none sent is fine (one area from X/Y/Width/Height).
    private static bool TryReadAreas(string? json, out IReadOnlyList<MockupAreaDto>? areas)
    {
        areas = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;
        try
        {
            areas = System.Text.Json.JsonSerializer.Deserialize<List<MockupAreaDto>>(json, AreasJson);
            return areas is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static IActionResult AreasNotReadable(ControllerBase controller)
    {
        controller.ModelState.AddModelError("areas", "The print areas could not be read.");
        return controller.ValidationProblem(controller.ModelState);
    }
}
