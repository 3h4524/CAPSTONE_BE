using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IVideoArtifactService
{
    Task<Result<IReadOnlyList<VideoResponse>>> ListAsync(Guid runId, CancellationToken ct);
    Task<Result<string>> DownloadAsync(Guid id, bool zip, CancellationToken ct);
}
public sealed class VideoArtifactService(ICurrentUser user, IRepository<WorkflowRun> runs, IRepository<PromoVideo> videos,
    IRepository<PromoVideoScene> scenes, IRepository<ExportPackage> exports, IMediaStorage storage, TimeProvider time) : IVideoArtifactService
{
    public async Task<Result<IReadOnlyList<VideoResponse>>> ListAsync(Guid runId, CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(runId, ct);
        if (run?.UserId != user.UserId) return Result.Failure<IReadOnlyList<VideoResponse>>(Missing());
        var result = new List<VideoResponse>();
        foreach (var video in (await videos.FindAsync(x => x.WorkflowRunId == runId, ct)).OrderByDescending(x => x.VersionNumber))
        {
            var boardElement = JsonSerializer.Deserialize<JsonElement>(video.ConfigSnapshot).GetProperty("storyboard");
            var board = boardElement.Deserialize<Storyboard>(WorkflowJson.Options) ?? throw new JsonException("Invalid video storyboard snapshot.");
            var output = VideoOutputFormats.Resolve(board.OutputFormat);
            var width = board.Width > 0 ? board.Width : output.Width;
            var height = board.Height > 0 ? board.Height : output.Height;
            var aspectRatio = string.IsNullOrWhiteSpace(board.AspectRatio) ? output.AspectRatio : board.AspectRatio;
            result.Add(new(video.Id, runId, video.Mode, board.Template, video.VersionNumber, video.ReviewRevision, video.Status,
                video.ApprovalStatus, video.StorageKey != null ? storage.SignRead(video.StorageKey, "video", video.StorageVersion) : null,
                video.ThumbnailStorageKey != null ? storage.SignRead(video.ThumbnailStorageKey, "image", video.ThumbnailStorageVersion) : null,
                JsonSerializer.Deserialize<JsonElement>(video.QaResult), (await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct)).OrderBy(x => x.SceneOrder).Select(x => WorkflowJson.Read<ScenePlan>(x.SceneConfig)).ToArray(),
                output.Format, width, height, aspectRatio));
        }
        return Result.Success<IReadOnlyList<VideoResponse>>(result);
    }
    public async Task<Result<string>> DownloadAsync(Guid id, bool zip, CancellationToken ct)
    {
        if (zip)
        {
            var package = await exports.GetByIdAsync(id, ct);
            if (package == null || package.UserId != user.UserId || package.Status != "ready" || package.ExpiresAt <= time.GetUtcNow().UtcDateTime || package.StorageKey == null)
                return Result.Failure<string>(Missing());
            return Result.Success(storage.SignRead(package.StorageKey, "raw", attachment: true));
        }
        var video = await videos.GetByIdAsync(id, ct);
        var run = video?.WorkflowRunId != null ? await runs.GetByIdAsync(video.WorkflowRunId.Value, ct) : null;
        if (run?.UserId != user.UserId || video?.StorageKey == null || video.Status != "completed") return Result.Failure<string>(Missing());
        return Result.Success(storage.SignRead(video.StorageKey, "video", video.StorageVersion, true));
    }
    private static Error Missing() => Error.NotFound("MediaNotFound", "Ready media not found.");
}
