using APCS.Api.Contracts.AdminStyleArtPresets;
using APCS.Api.Extensions;
using APCS.Application.Features.Admin.StyleArtPresets;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/admin/style-art-presets")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminStyleArtPresetsController(IAdminStyleArtPresetService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminStyleArtPresetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AdminStyleArtPresetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] CreateAdminStyleArtPresetForm form,
        CancellationToken cancellationToken)
    {
        var preview = form.Preview.ToUploadFileDto();
        try
        {
            var result = await service.CreateAsync(
                new CreateAdminStyleArtPresetRequestDto(
                    form.Name,
                    form.Description,
                    form.StyleModifiers,
                    form.RecommendationsJson,
                    preview,
                    ToViRequest(form.ViName, form.ViDescription, form.ViRecommendationsJson)),
                cancellationToken);

            return result.IsSuccess
                ? StatusCode(StatusCodes.Status201Created, result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            preview?.Content.Dispose();
        }
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AdminStyleArtPresetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] UpdateAdminStyleArtPresetForm form,
        CancellationToken cancellationToken)
    {
        var preview = form.Preview.ToUploadFileDto();
        try
        {
            var result = await service.UpdateAsync(
                id,
                new UpdateAdminStyleArtPresetRequestDto(
                    form.Name,
                    form.Description,
                    form.StyleModifiers,
                    form.RecommendationsJson,
                    preview,
                    form.DeletePreview,
                    form.IsActive,
                    ToViRequest(form.ViName, form.ViDescription, form.ViRecommendationsJson)),
                cancellationToken);

            return result.ToActionResult(this);
        }
        finally
        {
            preview?.Content.Dispose();
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToActionResult(this);

    private static AdminStyleArtPresetViRequestDto? ToViRequest(string? name, string? description, string? recommendationsJson) =>
        name is null && description is null && recommendationsJson is null
            ? null
            : new AdminStyleArtPresetViRequestDto(name, description, recommendationsJson);
}
