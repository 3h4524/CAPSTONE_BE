using APCS.Api.Contracts.StyleArtPresets;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using APCS.Application.Features.StyleArtPresets.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/style-art-presets")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class StyleArtPresetsController(IStyleArtPresetService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StyleArtPresetResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        (await service.ListMineAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StyleArtPresetResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await service.GetMineAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(StyleArtPresetResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromForm] CreateStyleArtPresetForm form,
        CancellationToken cancellationToken)
    {
        var preview = OpenPreview(form.Preview);
        try
        {
            var result = await service.CreateAsync(
                new CreateStyleArtPresetRequestDto(
                    form.Name,
                    form.Description,
                    form.StyleModifiers,
                    form.RecommendationsJson,
                    preview),
                cancellationToken);

            return result.IsSuccess
                ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            preview?.Content.Dispose();
        }
    }

    [HttpPut("{id:guid}")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(StyleArtPresetResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromForm] UpdateStyleArtPresetForm form,
        CancellationToken cancellationToken)
    {
        var preview = OpenPreview(form.Preview);
        try
        {
            var result = await service.UpdateAsync(
                id,
                new UpdateStyleArtPresetRequestDto(
                    form.Name,
                    form.Description,
                    form.StyleModifiers,
                    form.RecommendationsJson,
                    preview,
                    form.DeletePreview),
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToActionResult(this);

    private static UploadFileDto? OpenPreview(IFormFile? file) =>
        file is null
            ? null
            : new UploadFileDto(
                SanitizeFileName(file.FileName),
                file.ContentType,
                file.Length,
                file.OpenReadStream());

    private static string SanitizeFileName(string fileName) =>
        Path.GetFileName(fileName.Replace('\\', '/'));
}
