using System.Security.Claims;
using APCS.Api.Extensions;
using APCS.Application.Features.ImageQuality;
using APCS.Application.Features.ImageQuality.Dtos.Request;
using APCS.Application.Features.ImageQuality.Dtos.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Admin endpoints for the Image Quality page: offline scoring runs an admin uploads, and live stats of the design
/// images the jobs saved, split by the prompt version that made them.
/// </summary>
[ApiController]
[Route("api/admin/image-quality")]
[Authorize(Roles = "Admin,Super Admin")]
public sealed class AdminImageQualityController(IImageQualityService imageQualityService) : ControllerBase
{
    /// <summary>The largest export file accepted; thumbnails make up most of it.</summary>
    public const long MaximumUploadBytes = 20 * 1024 * 1024;

    /// <summary>Lists the stored scoring runs, newest first, without their images.</summary>
    [HttpGet("evaluations")]
    [ProducesResponseType(typeof(IReadOnlyList<ImageEvaluationSummaryResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvaluations(CancellationToken cancellationToken) =>
        (await imageQualityService.ListEvaluationsAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Gets one stored scoring run with every scored image.</summary>
    [HttpGet("evaluations/{id:guid}")]
    [ProducesResponseType(typeof(ImageEvaluationDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvaluation(Guid id, CancellationToken cancellationToken) =>
        (await imageQualityService.GetEvaluationAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Stores an uploaded scoring run and the thumbnails it carries.</summary>
    [HttpPost("evaluations")]
    [RequestSizeLimit(MaximumUploadBytes)]
    [ProducesResponseType(typeof(ImageEvaluationSummaryResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportEvaluation(
        [FromBody] ImportImageEvaluationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await imageQualityService.ImportEvaluationAsync(
            request,
            User.FindFirst(ClaimTypes.Email)?.Value,
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetEvaluation), new { id = result.Value.Id }, result.Value)
            : result.ToActionResult(this);
    }

    /// <summary>Removes a stored scoring run and its thumbnails.</summary>
    [HttpDelete("evaluations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEvaluation(Guid id, CancellationToken cancellationToken) =>
        (await imageQualityService.DeleteEvaluationAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Counts the design images saved in a period by image provider and prompt version.</summary>
    [HttpGet("live")]
    [ProducesResponseType(typeof(ImageQualityLiveStatsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLiveStats(
        [FromQuery] GetImageQualityLiveStatsRequestDto request,
        CancellationToken cancellationToken) =>
        (await imageQualityService.GetLiveStatsAsync(request, cancellationToken)).ToActionResult(this);
}
