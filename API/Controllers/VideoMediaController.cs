using System.Globalization;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api")]
public sealed class VideoMediaController(IMockupAssetService mockups, IVideoArtifactService media, IVideoPlanningService planning,
    ILifestyleMockupService lifestyle, IAiStudioFrameService studioFrames, IRepository<MusicTrack>? musicTracks = null) : ControllerBase
{
    [HttpGet("workflows/video-templates")]
    public async Task<IActionResult> Templates(CancellationToken ct) => (await planning.ListTemplatesAsync(ct)).ToActionResult(this);
    [HttpGet("workflows/ai-video-templates")]
    public async Task<IActionResult> AiTemplates(CancellationToken ct) => (await planning.ListAiTemplatesAsync(ct)).ToActionResult(this);
    [HttpGet("music-tracks")]
    public async Task<IActionResult> Music(CancellationToken ct)
    {
        if (musicTracks is null) return Ok(Array.Empty<object>());
        var tracks = await musicTracks.FindAsync(track => track.DeletedAt == null && track.IsAvailable == true && track.RoyaltyFree &&
            track.LicenseType != null && track.LicenseType != "" && track.AudioUrl != null && track.AudioUrl != "", ct);
        return Ok(tracks.OrderBy(track => track.Name).Select(track => new { track.Id, track.Name, track.ArtistName, track.DurationSeconds, track.Genre, track.Mood,
            License = track.LicenseType, PreviewUrl = track.PreviewUrl ?? track.AudioUrl }));
    }

    [HttpPost("products/{productId:guid}/video-storyboard")]
    public async Task<IActionResult> Storyboard(Guid productId, PreviewStoryboardRequest request, CancellationToken ct) => (await planning.PreviewAsync(productId, request, ct)).ToActionResult(this);
    [HttpGet("products/{productId:guid}/mockup-assets")]
    public async Task<IActionResult> List(Guid productId, CancellationToken ct) => (await mockups.ListAsync(productId, ct)).ToActionResult(this);
    [HttpPost("products/{productId:guid}/mockup-assets")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid productId, [FromForm] IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await mockups.UploadAsync(productId, content, file.FileName, file.Length, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }
    [HttpPost("products/{productId:guid}/mockup-assets/lifestyle")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(9 * 1024 * 1024)]
    public async Task<IActionResult> GenerateLifestyle(Guid productId, [FromForm] Guid garmentMockupId, [FromForm] IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await lifestyle.GenerateAsync(productId, garmentMockupId, content, file.Length, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }
    /// <summary>Prices a product studio keyframe and returns a matching reusable image when one exists.</summary>
    [HttpPost("products/{productId:guid}/studio-keyframe/quote")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [ProducesResponseType(typeof(AiStudioFrameQuote), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QuoteStudioFrameFromForm(Guid productId, [FromForm] AiStudioFrameFormRequest form, CancellationToken ct)
    {
        if (!form.TryGetConfirmedEstimatedCostUsd(out var confirmedEstimatedCostUsd))
            return BadRequest(new { message = "Confirmed estimated image cost must use an invariant decimal value, such as 0.04." });
        var request = await form.ToRequestAsync(confirmedEstimatedCostUsd, ct);
        if (request is null) return BadRequest(new { message = "Reference images must total 20 MB or less." });
        return (await studioFrames.QuoteAsync(productId, request, ct)).ToActionResult(this);
    }

    /// <summary>Generates one product studio keyframe after the user confirms its current quote.</summary>
    [HttpPost("products/{productId:guid}/studio-keyframe/generate")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [ProducesResponseType(typeof(AiStudioFrameGenerationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateStudioFrameFromForm(Guid productId, [FromForm] AiStudioFrameFormRequest form, CancellationToken ct)
    {
        if (!form.TryGetConfirmedEstimatedCostUsd(out var confirmedEstimatedCostUsd))
            return BadRequest(new { message = "Confirmed estimated image cost must use an invariant decimal value, such as 0.04." });
        var request = await form.ToRequestAsync(confirmedEstimatedCostUsd, ct);
        if (request is null) return BadRequest(new { message = "Reference images must total 20 MB or less." });
        var result = await studioFrames.GenerateAsync(productId, request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    // Preserve direct controller callers that already have an application request. These overloads
    // are not HTTP actions; the multipart endpoints above remain the only routed actions.
    [NonAction]
    public async Task<IActionResult> QuoteStudioFrame(Guid productId, AiStudioFrameRequest request, CancellationToken ct) =>
        (await studioFrames.QuoteAsync(productId, request, ct)).ToActionResult(this);

    [NonAction]
    public async Task<IActionResult> GenerateStudioFrame(Guid productId, AiStudioFrameRequest request, CancellationToken ct)
    {
        var result = await studioFrames.GenerateAsync(productId, request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    [HttpPut("mockup-assets/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MockupMetadataRequest request, CancellationToken ct) => (await mockups.UpdateAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("mockup-assets/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, MockupReviewRequest request, CancellationToken ct) => (await mockups.ReviewAsync(id, request, ct)).ToActionResult(this);
    [HttpGet("promo-videos/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct) => (await media.DownloadAsync(id, false, ct)).ToActionResult(this);
    [HttpGet("video-exports/{id:guid}/download")]
    public async Task<IActionResult> DownloadZip(Guid id, CancellationToken ct) => (await media.DownloadAsync(id, true, ct)).ToActionResult(this);
}

public sealed class AiStudioFrameFormRequest
{
    public Guid SourceMockupId { get; set; }
    public string Layout { get; set; } = "";
    public string OutputFormat { get; set; } = "";
    public bool Regenerate { get; set; }
    public string? ExpectedFingerprint { get; set; }
    public string? ConfirmedEstimatedCostUsd { get; set; }
    public string? CustomPrompt { get; set; }
    public string? TemplateCode { get; set; }
    public bool ReferenceRightsConfirmed { get; set; }
    public List<IFormFile> References { get; set; } = [];

    public bool TryGetConfirmedEstimatedCostUsd(out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(ConfirmedEstimatedCostUsd)) return true;

        if (!decimal.TryParse(ConfirmedEstimatedCostUsd.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;

        value = parsed;
        return true;
    }

    public async Task<AiStudioFrameRequest?> ToRequestAsync(decimal? confirmedEstimatedCostUsd, CancellationToken ct)
    {
        if (References.Sum(file => file.Length) > AiStudioFrameService.MaximumSourceBytes) return null;
        var references = new List<AiStudioFrameReferenceInput>(References.Count);
        foreach (var file in References)
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            references.Add(new AiStudioFrameReferenceInput(buffer.ToArray(), file.ContentType));
        }

        return new AiStudioFrameRequest(SourceMockupId, Layout, OutputFormat, Regenerate,
            ExpectedFingerprint, confirmedEstimatedCostUsd, CustomPrompt, TemplateCode, references,
            ReferenceRightsConfirmed);
    }
}
