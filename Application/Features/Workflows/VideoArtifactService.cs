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
    Task<Result<IReadOnlyList<AiSceneVideoResponse>>> ListSceneClipsAsync(Guid runId, CancellationToken ct);
    Task<Result<string>> DownloadAsync(Guid id, bool zip, CancellationToken ct);
    Task<Result<Stream>> OpenLocalExportAsync(Guid id, CancellationToken ct);
}
public sealed class VideoArtifactService(ICurrentUser user, IRepository<WorkflowRun> runs, IRepository<PromoVideo> videos,
    IRepository<PromoVideoScene> scenes, IRepository<ExportPackage> exports, IRepository<WorkflowNodeRun> nodes,
    IRepository<MediaJob> jobs, IMediaStorage storage, TimeProvider time, ILocalExportStore? localExports = null,
    IRepository<MockupImage>? mockups = null) : IVideoArtifactService
{
    public async Task<Result<IReadOnlyList<VideoResponse>>> ListAsync(Guid runId, CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(runId, ct);
        if (run is null || run.UserId != user.UserId)
            return Result.Failure<IReadOnlyList<VideoResponse>>(Missing());
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var mockupNode = definition.Nodes.SingleOrDefault(node => node.Type == "apply-mockup");
        var mockupConfig = mockupNode is null ? new MockupConfig() : WorkflowJson.Config<MockupConfig>(mockupNode);
        var result = new List<VideoResponse>();
        var clipPayload = await RenderPayloadAsync(runId, ct);
        foreach (var video in (await videos.FindAsync(x => x.WorkflowRunId == runId, ct)).OrderByDescending(x => x.VersionNumber))
        {
            var snapshot = JsonSerializer.Deserialize<JsonElement>(video.ConfigSnapshot);
            var boardElement = snapshot.GetProperty("storyboard");
            var board = boardElement.Deserialize<Storyboard>(WorkflowJson.Options) ?? throw new JsonException("Invalid video storyboard snapshot.");
            var config = snapshot.GetProperty("config").Deserialize<GenerateVideoConfig>(WorkflowJson.Options)
                ?? throw new JsonException("Invalid video configuration snapshot.");
            var output = VideoOutputFormats.Resolve(board.OutputFormat);
            var width = board.Width > 0 ? board.Width : output.Width;
            var height = board.Height > 0 ? board.Height : output.Height;
            var aspectRatio = string.IsNullOrWhiteSpace(board.AspectRatio) ? output.AspectRatio : board.AspectRatio;
            result.Add(new(video.Id, runId, video.Mode, board.Template, video.VersionNumber, video.ReviewRevision, video.Status,
                video.ApprovalStatus, video.StorageKey != null ? storage.SignRead(video.StorageKey, "video", video.StorageVersion) : null,
                video.ThumbnailStorageKey != null ? storage.SignRead(video.ThumbnailStorageKey, "image", video.ThumbnailStorageVersion) : null,
                JsonSerializer.Deserialize<JsonElement>(video.QaResult), (await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct)).OrderBy(x => x.SceneOrder).Select(x => WorkflowJson.Read<ScenePlan>(x.SceneConfig)).ToArray(),
                output.Format, width, height, aspectRatio, config, mockupConfig.ArtworkGroupKey, mockupConfig.MockupIds,
                // A rendered video keeps it too, so each of its scenes can still be shown on its own (the canvas previews them).
                video.Status is DeferredRender.VideoReady or DeferredRender.VideoRendering or DeferredRender.VideoCompleted
                    ? await PreviewAsync(video, board, clipPayload, ct) : null));
        }
        return Result.Success<IReadOnlyList<VideoResponse>>(result);
    }

    /// <summary>The links a browser needs to play a video from its scenes: its mock-ups, and the AI clips stored for it.</summary>
    private async Task<VideoPreviewData?> PreviewAsync(PromoVideo video, Storyboard board, AiRenderJobPayload? clipPayload, CancellationToken ct)
    {
        if (mockups is null) return null;
        var sources = new Dictionary<Guid, VideoPreviewSource>();
        foreach (var id in board.Scenes.Select(scene => scene.MockupId).Distinct())
        {
            var asset = await mockups.GetByIdAsync(id, ct);
            if (asset is null) return null;
            sources[id] = new(storage.SignRead(asset.StorageKey, "image", asset.StorageVersion), asset.MockupWidthPx, asset.MockupHeightPx);
        }
        var clips = new Dictionary<int, string>();
        if (clipPayload is { AiShotStates: not null } && clipPayload.VideoId == video.Id)
        {
            foreach (var shot in board.AiShots ?? [])
            {
                var state = clipPayload.AiShotStates.SingleOrDefault(x => x.SceneOrder == shot.SceneOrder && x.Fingerprint == shot.Fingerprint);
                if (state is { Status: "stored", StorageKey: not null, StorageVersion: not null })
                    clips[shot.SceneOrder] = storage.SignRead(state.StorageKey, "video", state.StorageVersion);
            }
        }
        return new(board, sources, clips);
    }

    /// <summary>The render job of the run's video, which holds the AI clips stored for it; null for a standard video or an unreadable job.</summary>
    private async Task<AiRenderJobPayload?> RenderPayloadAsync(Guid runId, CancellationToken ct)
    {
        var generate = (await nodes.FindAsync(x => x.WorkflowRunId == runId && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (generate is null) return null;
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == generate.Id && x.Kind == "render", ct)).SingleOrDefault();
        if (job is null) return null;
        try { return WorkflowJson.Read<AiRenderJobPayload>(job.Payload); }
        catch (JsonException) { return null; }
    }
    public async Task<Result<IReadOnlyList<AiSceneVideoResponse>>> ListSceneClipsAsync(Guid runId, CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(runId, ct);
        if (run?.UserId != user.UserId) return Result.Failure<IReadOnlyList<AiSceneVideoResponse>>(Missing());
        var generate = (await nodes.FindAsync(x => x.WorkflowRunId == runId && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (generate == null) return Result.Success<IReadOnlyList<AiSceneVideoResponse>>([]);
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == generate.Id && x.Kind == "render", ct)).SingleOrDefault();
        if (job is null) return Result.Success<IReadOnlyList<AiSceneVideoResponse>>([]);
        try
        {
            var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
            if (payload.Storyboard == null || payload.AiShotStates == null)
                return Result.Success<IReadOnlyList<AiSceneVideoResponse>>([]);
            var clips = (payload.Storyboard.AiShots ?? []).OrderBy(x => x.SceneOrder).Select(shot =>
            {
                var state = payload.AiShotStates.SingleOrDefault(x => x.SceneOrder == shot.SceneOrder && x.Fingerprint == shot.Fingerprint);
                var preview = state is { Status: "stored", StorageKey: not null, StorageVersion: not null }
                    ? storage.SignRead(state.StorageKey, "video", state.StorageVersion)
                    : null;
                return new AiSceneVideoResponse(shot.SceneOrder, state?.Status ?? "pending", preview, state?.Failure);
            }).ToArray();
            return Result.Success<IReadOnlyList<AiSceneVideoResponse>>(clips);
        }
        catch (JsonException)
        {
            return Result.Success<IReadOnlyList<AiSceneVideoResponse>>([]);
        }
    }
    public async Task<Result<string>> DownloadAsync(Guid id, bool zip, CancellationToken ct)
    {
        if (zip)
        {
            var package = await exports.GetByIdAsync(id, ct);
            if (package == null || package.UserId != user.UserId || package.Status != "ready" || package.ExpiresAt <= time.GetUtcNow().UtcDateTime || package.StorageKey == null)
                return Result.Failure<string>(Missing());
            if (package.StorageProvider == "local") return Result.Success("local");
            return Result.Success(storage.SignRead(package.StorageKey, "raw", attachment: true));
        }
        var video = await videos.GetByIdAsync(id, ct);
        var run = video?.WorkflowRunId != null ? await runs.GetByIdAsync(video.WorkflowRunId.Value, ct) : null;
        if (run?.UserId != user.UserId || video?.StorageKey == null || video.Status != "completed") return Result.Failure<string>(Missing());
        return Result.Success(storage.SignRead(video.StorageKey, "video", video.StorageVersion, true));
    }
    public async Task<Result<Stream>> OpenLocalExportAsync(Guid id, CancellationToken ct)
    {
        var package = await exports.GetByIdAsync(id, ct);
        if (package is not { StorageProvider: "local", Status: "ready", StorageKey: not null } ||
            package.UserId != user.UserId || package.ExpiresAt <= time.GetUtcNow().UtcDateTime || localExports is null)
            return Result.Failure<Stream>(Missing());
        var stream = await localExports.OpenReadAsync(package.StorageKey, ct);
        return stream is null ? Result.Failure<Stream>(Missing()) : Result.Success(stream);
    }
    private static Error Missing() => Error.NotFound("MediaNotFound", "Ready media not found.");
}
