using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api")]
public sealed class VideoMediaController(IMockupAssetService mockups, IVideoArtifactService media, IVideoPlanningService planning) : ControllerBase
{
    [HttpGet("workflows/video-templates")]
    public async Task<IActionResult> Templates(CancellationToken ct) => (await planning.ListTemplatesAsync(ct)).ToActionResult(this);

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
    [HttpPut("mockup-assets/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MockupMetadataRequest request, CancellationToken ct) => (await mockups.UpdateAsync(id, request, ct)).ToActionResult(this);
    [HttpPost("mockup-assets/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, MockupReviewRequest request, CancellationToken ct) => (await mockups.ReviewAsync(id, request, ct)).ToActionResult(this);
    [HttpGet("promo-videos/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct) => (await media.DownloadAsync(id, false, ct)).ToActionResult(this);
    [HttpGet("video-exports/{id:guid}/download")]
    public async Task<IActionResult> DownloadZip(Guid id, CancellationToken ct) => (await media.DownloadAsync(id, true, ct)).ToActionResult(this);
}
