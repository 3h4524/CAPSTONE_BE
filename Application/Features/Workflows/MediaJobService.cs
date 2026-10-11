using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IMediaJobService
{
    Task<Result<WorkerJob?>> ClaimAsync(CancellationToken ct);
    Task<Result> HeartbeatAsync(Guid id, JobHeartbeat request, CancellationToken ct);
    Task<Result> CompleteAsync(Guid id, JobCompletion request, CancellationToken ct);
    Task<Result> FailAsync(Guid id, JobFailure request, CancellationToken ct);
}

public sealed class MediaJobService(IRepository<MediaJob> jobs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<ExportPackage> exports, IRepository<ExportPackageItem> items,
    IWorkflowStateRepository state, IUnitOfWork uow, IMediaStorage storage, WorkflowRuntime runtime, TimeProvider time) : IMediaJobService
{
    public async Task<Result<WorkerJob?>> ClaimAsync(CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var token = Guid.NewGuid();
        var job = await state.ClaimAsync(time.GetUtcNow().UtcDateTime, token, ct);
        if (job == null) { await tx.CommitAsync(ct); return Result.Success<WorkerJob?>(null); }
        await tx.CommitAsync(ct);
        var workerAssets = new List<WorkerAsset>();
        var uploads = new Dictionary<string, UploadGrant>();
        if (job.Kind == "render")
        {
            var payload = WorkflowJson.Read<RenderJobPayload>(job.Payload);
            foreach (var scene in payload.Storyboard.Scenes.DistinctBy(x => x.MockupId))
            {
                var asset = await assets.GetByIdAsync(scene.MockupId, ct);
                if (asset == null || asset.MetadataRevision != scene.SourceRevision || asset.ContentHash != scene.SourceHash || asset.ApprovalStatus != "approved" || asset.ApprovedRevision != scene.SourceRevision)
                { await FailAsync(job.Id, new(token, "Source mockup approval or revision changed. Review mockups again."), ct); return Result.Success<WorkerJob?>(null); }
                workerAssets.Add(new(asset.Id, storage.SignRead(asset.StorageKey, "image", asset.StorageVersion), $"mockups/{asset.Id}.png"));
            }
            uploads["video"] = storage.CreateUploadGrant(OutputKey(job, token, "video"), "video");
            uploads["thumbnail"] = storage.CreateUploadGrant(OutputKey(job, token, "thumbnail"), "image");
        }
        else
        {
            var payload = WorkflowJson.Read<ExportJobPayload>(job.Payload);
            var video = await videos.GetByIdAsync(payload.VideoId, ct);
            if (video?.ApprovalStatus != "approved") { await FailAsync(job.Id, new(token, "Video is no longer approved."), ct); return Result.Success<WorkerJob?>(null); }
            foreach (var scene in (await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct)).DistinctBy(x => x.MockupImageId))
            {
                var asset = scene.MockupImageId.HasValue ? await assets.GetByIdAsync(scene.MockupImageId.Value, ct) : null;
                if (asset == null || asset.MetadataRevision != scene.SourceRevision || asset.ContentHash != scene.SourceHash || asset.ApprovalStatus != "approved" || asset.ApprovedRevision != scene.SourceRevision)
                { await FailAsync(job.Id, new(token, "Source mockup is no longer approved at the rendered revision."), ct); return Result.Success<WorkerJob?>(null); }
                workerAssets.Add(new(asset.Id, storage.SignRead(asset.StorageKey, "image", asset.StorageVersion), $"mockups/{asset.Id}.png"));
            }
            workerAssets.Add(new(Guid.Empty, storage.SignRead(payload.VideoStorageKey, "video", payload.VideoStorageVersion), "video.mp4"));
            workerAssets.Add(new(Guid.Empty, storage.SignRead(payload.ThumbnailStorageKey, "image", payload.ThumbnailStorageVersion), "thumbnail.png"));
            uploads["zip"] = storage.CreateUploadGrant(OutputKey(job, token, "package.zip"), "raw");
        }
        return Result.Success<WorkerJob?>(new(job.Id, token, job.Kind, JsonSerializer.Deserialize<JsonElement>(job.Payload), workerAssets, uploads));
    }
    public async Task<Result> HeartbeatAsync(Guid id, JobHeartbeat request, CancellationToken ct)
    {
        if (request.Progress is < 0 or > 100 || !new[] { "Planning", "Generating Assets", "Rendering", "Technical QA", "Uploading", "Exporting" }.Contains(request.Stage))
            return Result.Failure(Error.Validation("Invalid progress checkpoint."));
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null) return Result.Failure(LeaseError());
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, request.LeaseToken, run)) return Result.Failure(LeaseError());
        job!.Progress = Math.Max(job.Progress, request.Progress); job.Stage = request.Stage; job.HeartbeatAt = time.GetUtcNow().UtcDateTime;
        job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(2).UtcDateTime;
        var node = (await nodes.GetByIdAsync(job.WorkflowNodeRunId, ct))!; node.Progress = job.Progress; node.Stage = job.Stage;
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success();
    }
    public async Task<Result> CompleteAsync(Guid id, JobCompletion request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null) return Result.Failure(LeaseError());
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, request.LeaseToken, run)) return Result.Failure(LeaseError());
        var expected = OutputKey(job!, request.LeaseToken, job!.Kind == "render" ? "video" : "package.zip");
        if (request.StorageKey != expected || request.StorageVersion.Length > 40 ||
            !await storage.VerifyAsync(expected, job.Kind == "render" ? "video" : "raw", request.StorageVersion, request.Qa?.Bytes, ct))
            return Result.Failure(Error.Validation("Worker output does not match its upload grant."));
        if (job.Kind == "render")
        {
            var payload = WorkflowJson.Read<RenderJobPayload>(job.Payload);
            var output = VideoOutputFormats.Resolve(payload.Storyboard.OutputFormat);
            var width = payload.Storyboard.Width > 0 ? payload.Storyboard.Width : output.Width;
            var height = payload.Storyboard.Height > 0 ? payload.Storyboard.Height : output.Height;
            if (!ValidQa(request.Qa, payload.Storyboard.DurationSeconds, width, height)) return Result.Failure(Error.Validation("Video failed technical QA."));
            var thumb = OutputKey(job, request.LeaseToken, "thumbnail");
            if (request.ThumbnailStorageKey != thumb || string.IsNullOrEmpty(request.ThumbnailStorageVersion) ||
                !await storage.VerifyAsync(thumb, "image", request.ThumbnailStorageVersion, null, ct)) return Result.Failure(Error.Validation("Thumbnail verification failed."));
            foreach (var scene in payload.Storyboard.Scenes.DistinctBy(x => x.MockupId))
            {
                var asset = await state.LockMockupAsync(scene.MockupId, ct);
                if (asset?.ApprovedRevision != scene.SourceRevision || asset.MetadataRevision != scene.SourceRevision || asset.ApprovalStatus != "approved")
                    return Result.Failure(Error.Conflict("SourceChanged", "Source mockup approval changed during render."));
            }
            var video = (await videos.GetByIdAsync(payload.VideoId, ct))!;
            video.Status = "completed"; video.StorageProvider = "cloudinary"; video.StorageKey = expected; video.StorageVersion = request.StorageVersion;
            video.ThumbnailStorageKey = thumb; video.ThumbnailStorageVersion = request.ThumbnailStorageVersion; video.QaResult = WorkflowJson.Write(request.Qa);
            video.FileSizeMb = request.Qa!.Bytes / (1024m * 1024m); video.UpdatedAt = time.GetUtcNow().UtcDateTime;
        }
        else
        {
            var payload = WorkflowJson.Read<ExportJobPayload>(job.Payload);
            var video = await videos.GetByIdAsync(payload.VideoId, ct);
            if (video?.ApprovalStatus != "approved") return Result.Failure(Error.Conflict("NotApproved", "Video approval was revoked."));
            foreach (var scene in await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct))
            {
                var asset = scene.MockupImageId.HasValue ? await state.LockMockupAsync(scene.MockupImageId.Value, ct) : null;
                if (asset?.ApprovedRevision != scene.SourceRevision || asset?.MetadataRevision != scene.SourceRevision || asset?.ApprovalStatus != "approved")
                    return Result.Failure(Error.Conflict("SourceChanged", "Source mockup approval changed during export."));
            }
            var package = (await exports.GetByIdAsync(payload.ExportId, ct))!;
            package.Status = "ready"; package.StorageProvider = "cloudinary"; package.StorageKey = expected;
            foreach (var item in await items.FindAsync(x => x.ExportPackageId == package.Id, ct)) item.ItemStatus = "completed";
        }
        job.Status = "succeeded"; job.Progress = 100; job.LeaseToken = null; job.LeaseExpiresAt = null;
        var node = (await nodes.GetByIdAsync(job.WorkflowNodeRunId, ct))!; node.Status = "succeeded"; node.Progress = 100;
        run!.Revision++; run.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct); await runtime.AdvanceAsync(run, ct); await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Result.Success();
    }
    public async Task<Result> FailAsync(Guid id, JobFailure request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null) return Result.Failure(LeaseError());
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, request.LeaseToken, run)) return Result.Failure(LeaseError());
        job!.ErrorMessage = "Worker could not finish media processing. Retry or review source assets.";
        job.LeaseToken = null; job.LeaseExpiresAt = null;
        job.Status = job.Attempt < job.MaximumAttempts ? "queued" : "failed";
        job.AvailableAt = time.GetUtcNow().AddSeconds(5 * job.Attempt).UtcDateTime;
        if (job.Status == "failed") await runtime.FailAsync(run!, job.WorkflowNodeRunId, job.ErrorMessage, ct);
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result.Success();
    }
    private bool ValidLease(MediaJob? job, Guid token, WorkflowRun? run) => job is { Status: "leased" } && job.LeaseToken == token &&
        job.LeaseExpiresAt > time.GetUtcNow().UtcDateTime && run?.Status == "running";
    private static Error LeaseError() => Error.Conflict("LeaseLost", "Job lease expired or run was cancelled.");
    private static string OutputKey(MediaJob job, Guid token, string suffix) => $"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/{token}/{suffix}";
    public static bool ValidQa(MediaQa? qa, int duration, int width = 1080, int height = 2160) => qa != null && qa.Codec == "h264" && qa.PixelFormat == "yuv420p" &&
        qa.Width == width && qa.Height == height && Math.Abs(qa.Fps - 30) < .01 && Math.Abs(qa.DurationSeconds - duration) <= 1d / 30 + .01 &&
        qa.Bytes is > 0 and < 100 * 1024 * 1024 && !qa.HasAudio && qa.FullDecode;
}
