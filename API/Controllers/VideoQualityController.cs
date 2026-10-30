using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Lets a seller evaluate and review one already-generated AI video scene.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/video-quality")]
public sealed class VideoQualityController(IVideoQualityReviewService quality) : ControllerBase
{
    /// <summary>Returns saved local-tool evidence and seller observations for a scene.</summary>
    [HttpGet("runs/{runId:guid}/videos/{videoId:guid}/scenes/{sceneOrder:int}")]
    public async Task<IActionResult> Get(Guid runId, Guid videoId, int sceneOrder, CancellationToken cancellationToken) =>
        (await quality.GetAsync(runId, videoId, sceneOrder, cancellationToken)).ToActionResult(this);

    /// <summary>Runs configured local PaddleOCR and VBench adapters for the stored scene clip.</summary>
    [HttpPost("runs/{runId:guid}/videos/{videoId:guid}/scenes/{sceneOrder:int}/evaluate")]
    public async Task<IActionResult> Evaluate(Guid runId, Guid videoId, int sceneOrder, CancellationToken cancellationToken) =>
        (await quality.EvaluateAsync(runId, videoId, sceneOrder, cancellationToken)).ToActionResult(this);

    /// <summary>Saves a timestamped seller observation and the correction target they confirmed.</summary>
    [HttpPut("runs/{runId:guid}/videos/{videoId:guid}/scenes/{sceneOrder:int}/reviews")]
    public async Task<IActionResult> SaveReview(Guid runId, Guid videoId, int sceneOrder,
        SaveVideoSceneReviewRequest request, CancellationToken cancellationToken) =>
        (await quality.SaveReviewAsync(runId, videoId, sceneOrder, request, cancellationToken)).ToActionResult(this);

    /// <summary>Confirms, rejects, or retires a private video lesson created from a review.</summary>
    [HttpPut("lessons/{lessonId:guid}/status")]
    public async Task<IActionResult> SetLessonStatus(Guid lessonId, SetVideoExperienceStatusRequest request,
        CancellationToken cancellationToken) =>
        (await quality.SetLessonStatusAsync(lessonId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Retires the current lesson and creates a reviewed candidate revision without overwriting history.</summary>
    [HttpPut("runs/{runId:guid}/videos/{videoId:guid}/scenes/{sceneOrder:int}/lessons/{lessonId:guid}/revision")]
    public async Task<IActionResult> ReviseLesson(Guid runId, Guid videoId, int sceneOrder, Guid lessonId,
        ReviseVideoExperienceRequest request, CancellationToken cancellationToken) =>
        (await quality.ReviseLessonAsync(runId, videoId, sceneOrder, lessonId, request, cancellationToken)).ToActionResult(this);
}
