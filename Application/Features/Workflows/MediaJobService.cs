using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IMediaJobService
{
    Task<Result<WorkerJob?>> ClaimAsync(CancellationToken ct);
    Task<Result<MediaStreamClaimResponse>> ClaimStreamAsync(Guid jobId, long queueVersion, CancellationToken ct);
    Task<Result> HeartbeatAsync(Guid id, JobHeartbeat request, CancellationToken ct);
    Task<Result<LocalExportFile>> UploadExportAsync(Guid id, Guid leaseToken, Stream content, CancellationToken ct);
    Task<Result> CompleteAsync(Guid id, JobCompletion request, CancellationToken ct);
    Task<Result> FailAsync(Guid id, JobFailure request, CancellationToken ct);
}

public sealed class MediaJobService(IRepository<MediaJob> jobs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<ExportPackage> exports, IRepository<ExportPackageItem> items,
    IWorkflowStateRepository state, IUnitOfWork uow, IMediaStorage storage, WorkflowRuntime runtime, TimeProvider time,
    IRepository<MusicTrack>? musicTracks = null, IVideoQualityJobService? qualityJobs = null,
    ILocalExportStore? localExports = null, IRepository<DesignImage>? designImages = null,
    ISceneVideoNotifier? sceneNotifier = null, IMediaLeaseRecovery? leaseRecovery = null,
    IWorkflowRunNotifier? runNotifier = null) : IMediaJobService
{
    public async Task<Result<WorkerJob?>> ClaimAsync(CancellationToken ct)
        => await ClaimCoreAsync(null, null, ct);

    public async Task<Result<MediaStreamClaimResponse>> ClaimStreamAsync(Guid jobId, long queueVersion, CancellationToken ct)
    {
        if (jobId == Guid.Empty || queueVersion <= 0)
            return Result.Failure<MediaStreamClaimResponse>(Error.Validation("A valid job ID and queue version are required."));
        var claimed = await ClaimCoreAsync(jobId, queueVersion, ct);
        if (claimed.IsFailure) return Result.Failure<MediaStreamClaimResponse>(claimed.Error);
        if (claimed.Value is not null) return Result.Success(new MediaStreamClaimResponse("claimed", claimed.Value));
        var status = await state.GetMediaClaimStatusAsync(jobId, queueVersion, time.GetUtcNow().UtcDateTime, ct);
        return Result.Success(new MediaStreamClaimResponse(status, null));
    }

    private async Task<Result<WorkerJob?>> ClaimCoreAsync(Guid? jobId, long? queueVersion, CancellationToken ct)
    {
        // A polling worker claims from the database, so expired leases are settled first, each with its run. The
        // Streams dispatcher does this on its own recovery pass.
        if (jobId is null && leaseRecovery != null)
            await leaseRecovery.RecoverExpiredAsync(time.GetUtcNow().UtcDateTime, ct);
        await using var tx = await uow.BeginTransactionAsync(ct);
        var token = Guid.NewGuid();
        var job = jobId is Guid id && queueVersion is long version
            ? await state.ClaimMediaAsync(id, version, time.GetUtcNow().UtcDateTime, token, ct)
            : await state.ClaimAsync(time.GetUtcNow().UtcDateTime, token, ct);
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
            if (payload.Storyboard.Mode == "ai_shot")
            {
                var ai = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
                foreach (var shot in ai.Storyboard.AiShots ?? [])
                {
                    var clip = ai.AiShotStates.SingleOrDefault(x => x.SceneOrder == shot.SceneOrder && x.Fingerprint == shot.Fingerprint);
                    if (clip is { Status: "stored", StorageKey: not null, StorageVersion: not null })
                        workerAssets.Add(new(Guid.Empty, storage.SignRead(clip.StorageKey, "video", clip.StorageVersion), $"ai-shots/{shot.SceneOrder}.mp4"));
                    else uploads[$"ai-shot-{shot.SceneOrder}"] = storage.CreateUploadGrant(AiShotJobService.OutputKey(job, token, shot.SceneOrder), "video");
                }
            }
            if (payload.Storyboard.VideoEdit?.Music is { } music)
            {
                var track = musicTracks is null ? null : await musicTracks.GetByIdAsync(music.TrackId, ct);
                if (track is not { IsAvailable: true, RoyaltyFree: true, DeletedAt: null } || string.IsNullOrWhiteSpace(track.LicenseType) || string.IsNullOrWhiteSpace(track.AudioUrl))
                { await FailAsync(job.Id, new(token, "Selected music track is no longer available. Choose another track."), ct); return Result.Success<WorkerJob?>(null); }
                workerAssets.Add(new(Guid.Empty, track.AudioUrl, $"music/{track.Id}.mp3"));
            }
            uploads["video"] = storage.CreateUploadGrant(OutputKey(job, token, "video"), "video");
            uploads["thumbnail"] = storage.CreateUploadGrant(OutputKey(job, token, "thumbnail"), "image");
        }
        else
        {
            var payload = WorkflowJson.Read<ExportJobPayload>(job.Payload);
            var video = await videos.GetByIdAsync(payload.VideoId, ct);
            if (video?.ApprovalStatus != "approved") { await FailAsync(job.Id, new(token, "Video is no longer approved."), ct); return Result.Success<WorkerJob?>(null); }
            var packageItems = await items.FindAsync(x => x.ExportPackageId == payload.ExportId, ct);
            var productId = packageItems.Select(x => x.ProductId).Distinct().SingleOrDefault();
            var approvedSnapshot = payload.ApprovedMockups ?? [];
            if (approvedSnapshot.Count == 0)
            {
                approvedSnapshot = (await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct)).Where(x => x.MockupImageId.HasValue)
                    .Select(x => new ExportAssetSnapshot(x.MockupImageId!.Value, "", null, x.SourceHash, x.SourceRevision ?? 0)).DistinctBy(x => x.Id).ToArray();
            }
            foreach (var snapshot in approvedSnapshot.DistinctBy(x => x.Id))
            {
                var asset = await assets.GetByIdAsync(snapshot.Id, ct);
                if (asset == null || asset.ProductId != productId || asset.MetadataRevision != snapshot.Revision || asset.ContentHash != snapshot.Sha256 ||
                    asset.ApprovalStatus != "approved" || asset.ApprovedRevision != snapshot.Revision ||
                    snapshot.StorageKey.Length > 0 && asset.StorageKey != snapshot.StorageKey ||
                    snapshot.StorageVersion != null && asset.StorageVersion != snapshot.StorageVersion)
                { await FailAsync(job.Id, new(token, "Source mockup is no longer approved at the rendered revision."), ct); return Result.Success<WorkerJob?>(null); }
                workerAssets.Add(new(asset.Id, storage.SignRead(asset.StorageKey, "image", asset.StorageVersion), $"mockups/{asset.Id}.png"));
            }
            foreach (var design in payload.Designs ?? [])
            {
                var source = designImages is null ? null : await designImages.GetByIdAsync(design.Id, ct);
                if (source == null || source.ProductId != productId || source.DeletedAt != null || source.ApprovalStatus != "approved" ||
                    source.StorageKey != design.StorageKey || design.FileFormat is not null && source.FileFormat != design.FileFormat ||
                    !Uri.TryCreate(source.ImageUrl, UriKind.Absolute, out var designUri) || designUri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(designUri.Host, "res.cloudinary.com", StringComparison.OrdinalIgnoreCase) ||
                    !designUri.AbsolutePath.Contains("/image/upload/", StringComparison.Ordinal))
                { await FailAsync(job.Id, new(token, "An approved design is no longer available."), ct); return Result.Success<WorkerJob?>(null); }
                workerAssets.Add(new(source.Id, source.ImageUrl, $"designs/{source.Id}"));
            }
            if (payload.Sources is { } sources && sources.VideoId == video.Id && sources.StoryboardFingerprint == payload.Storyboard?.Fingerprint)
            {
                foreach (var sceneSource in sources.Scenes)
                {
                    var sceneAsset = await assets.GetByIdAsync(sceneSource.MockupId, ct);
                    if (sceneAsset == null || sceneAsset.ProductId != productId || sceneAsset.MetadataRevision != sceneSource.SourceRevision ||
                        sceneAsset.ApprovedRevision != sceneSource.SourceRevision || sceneAsset.ApprovalStatus != "approved" ||
                        sceneAsset.ContentHash != sceneSource.SourceHash || sceneAsset.StorageKey != sceneSource.ImageStorageKey ||
                        sceneAsset.StorageVersion != sceneSource.ImageStorageVersion)
                    { await FailAsync(job.Id, new(token, "A video scene source is no longer approved at its original revision."), ct); return Result.Success<WorkerJob?>(null); }
                    workerAssets.Add(new(sceneSource.MockupId, storage.SignRead(sceneSource.ImageStorageKey, "image", sceneSource.ImageStorageVersion),
                        $"scene-sources/{sceneSource.SceneOrder}.png"));
                    if (sceneSource.ClipStorageKey is not null && sceneSource.ClipStorageVersion is not null)
                        workerAssets.Add(new(Guid.Empty, storage.SignRead(sceneSource.ClipStorageKey, "video", sceneSource.ClipStorageVersion),
                            $"scene-clips/{sceneSource.SceneOrder}.mp4"));
                }
            }
            // Model photo images ride along as they were when the package was planned, and only while still approved.
            if (payload.Manifest.TryGetProperty("modelPhotos", out var modelPhotos) && modelPhotos.ValueKind == JsonValueKind.Array)
            {
                foreach (var photo in modelPhotos.EnumerateArray())
                {
                    var asset = photo.TryGetProperty("mockupId", out var photoId) && photoId.TryGetGuid(out var mockupId) ? await assets.GetByIdAsync(mockupId, ct) : null;
                    if (asset == null || asset.DeletedAt != null || asset.ApprovalStatus != "approved"
                        || !photo.TryGetProperty("sourceHash", out var hash) || asset.ContentHash != hash.GetString())
                    { await FailAsync(job.Id, new(token, "A model photo image changed after the package was planned. Export again."), ct); return Result.Success<WorkerJob?>(null); }
                    workerAssets.Add(new(asset.Id, storage.SignRead(asset.StorageKey, "image", asset.StorageVersion), $"model-photos/{asset.Id}.png"));
                }
            }
            workerAssets.Add(new(Guid.Empty, storage.SignRead(payload.VideoStorageKey, "video", payload.VideoStorageVersion), "video.mp4"));
            workerAssets.Add(new(Guid.Empty, storage.SignRead(payload.ThumbnailStorageKey, "image", payload.ThumbnailStorageVersion), "thumbnail.png"));
        }
        return Result.Success<WorkerJob?>(new(job.Id, token, job.Kind, JsonSerializer.Deserialize<JsonElement>(job.Payload), workerAssets, uploads));
    }
    public async Task<Result<LocalExportFile>> UploadExportAsync(Guid id, Guid leaseToken, Stream content, CancellationToken ct)
    {
        if (localExports is null) return Result.Failure<LocalExportFile>(Error.Failure("LocalExportUnavailable", "Local ZIP export storage is unavailable."));
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked is null) return Result.Failure<LocalExportFile>(LeaseError());
        await using var tx = await uow.BeginTransactionAsync(ct);
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, leaseToken, run) || job!.Kind != "export_zip") return Result.Failure<LocalExportFile>(LeaseError());
        // Renew the lease before accepting a streamed package; CompleteAsync validates it again before publishing.
        job.LeaseExpiresAt = time.GetUtcNow().AddMinutes(2).UtcDateTime;
        job.HeartbeatAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        try
        {
            return Result.Success(await localExports.SaveAsync(id, leaseToken, content, ct));
        }
        catch (InvalidDataException exception)
        {
            return Result.Failure<LocalExportFile>(Error.Validation(exception.Message));
        }
        catch (IOException)
        {
            return Result.Failure<LocalExportFile>(Error.Failure("LocalExportWriteFailed", "The ZIP could not be saved on the API server."));
        }
    }
    public async Task<Result> HeartbeatAsync(Guid id, JobHeartbeat request, CancellationToken ct)
    {
        if (request.Progress is < 0 or > 100 || !new[] { "Planning", "Generating Assets", "Rendering", "Technical QA", "Uploading", "Exporting" }.Contains(request.Stage))
            return Result.Failure(Error.Validation("Invalid progress checkpoint."));
        // Every render sends one every 15 seconds: one statement that locks only the job, not the run the seller's
        // page reads, and the canvas step is written only when there is something new to show.
        var now = time.GetUtcNow().UtcDateTime;
        var renewal = await state.RenewLeaseAsync(id, request.LeaseToken, request.Stage, request.Progress, now, now.AddMinutes(2), ProgressStep, ct);
        if (renewal is null) return Result.Failure(LeaseError());
        if (renewal.StepWritten && runNotifier != null)
            await runNotifier.ProgressAsync(renewal.RunId, renewal.RunGroupId, renewal.Stage, renewal.Progress, ct);
        return Result.Success();
    }

    /// <summary>Progress, in points, a heartbeat has to add before the canvas step is written again.</summary>
    public const int ProgressStep = 5;
    public async Task<Result> CompleteAsync(Guid id, JobCompletion request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null) return Result.Failure(LeaseError());
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, request.LeaseToken, run)) return Result.Failure(LeaseError());
        var expected = job!.Kind == "render" ? OutputKey(job, request.LeaseToken, "video") : $"local-export/{job.Id:D}/{request.LeaseToken:D}";
        var outputVerified = job.Kind == "render"
            ? request.StorageKey == expected && request.StorageVersion.Length <= 40 &&
                await storage.VerifyAsync(expected, "video", request.StorageVersion, request.Qa?.Bytes, ct)
            : localExports is not null && request.StorageKey == expected && request.StorageVersion.Length == 64 &&
                await localExports.VerifyAsync(expected, request.StorageVersion, request.Qa?.Bytes ?? request.OutputBytes, ct);
        if (!outputVerified)
            return Result.Failure(Error.Validation("Worker output does not match its upload grant."));
        var queueAutomaticQuality = false;
        Guid qualityVideoId = Guid.Empty;
        if (job.Kind == "render")
        {
            var payload = WorkflowJson.Read<RenderJobPayload>(job.Payload);
            AiRenderJobPayload? aiPayload = null;
            if (payload.Storyboard.Mode == "ai_shot")
            {
                aiPayload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
                if ((aiPayload.Storyboard.AiShots ?? []).Any(shot => !aiPayload.AiShotStates.Any(x => x.SceneOrder == shot.SceneOrder && x.Fingerprint == shot.Fingerprint && x.Status == "stored" && x.StorageKey != null)))
                    return Result.Failure(Error.Conflict("AiShotIncomplete", "All AI Shot clips must be stored before render completion."));
            }
            var output = VideoOutputFormats.Resolve(payload.Storyboard.OutputFormat);
            var width = payload.Storyboard.Width > 0 ? payload.Storyboard.Width : output.Width;
            var height = payload.Storyboard.Height > 0 ? payload.Storyboard.Height : output.Height;
            var outputDurationSeconds = (payload.Storyboard.OutputDurationFrames ?? payload.Storyboard.DurationSeconds * 30) / 30d;
            var expectsAudio = payload.Storyboard.VideoEdit?.Music is { Muted: false };
            if (!ValidQa(request.Qa, outputDurationSeconds, width, height, expectsAudio)) return Result.Failure(Error.Validation("Video failed technical QA."));
            var thumb = OutputKey(job, request.LeaseToken, "thumbnail");
            if (request.ThumbnailStorageKey != thumb || string.IsNullOrEmpty(request.ThumbnailStorageVersion) ||
                !await storage.VerifyAsync(thumb, "image", request.ThumbnailStorageVersion, null, ct)) return Result.Failure(Error.Validation("Thumbnail verification failed."));
            var sourceAssets = new Dictionary<Guid, MockupImage>();
            foreach (var scene in payload.Storyboard.Scenes.DistinctBy(x => x.MockupId))
            {
                var asset = await state.LockMockupAsync(scene.MockupId, ct);
                if (asset?.ApprovedRevision != scene.SourceRevision || asset.MetadataRevision != scene.SourceRevision ||
                    asset.ApprovalStatus != "approved" || asset.ContentHash != scene.SourceHash)
                    return Result.Failure(Error.Conflict("SourceChanged", "Source mockup approval changed during render."));
                sourceAssets.Add(scene.MockupId, asset);
            }
            if (payload.Storyboard.Scenes.Any(scene =>
                sourceAssets[scene.MockupId].MetadataRevision != scene.SourceRevision ||
                sourceAssets[scene.MockupId].ContentHash != scene.SourceHash))
                return Result.Failure(Error.Conflict("SourceChanged", "Source mockup approval changed during render."));
            var video = (await videos.GetByIdAsync(payload.VideoId, ct))!;
            if (ReadConfigSnapshot(video.ConfigSnapshot) is { } snapshot)
            {
                var sceneSources = payload.Storyboard.Scenes.Select(scene =>
                {
                    var source = sourceAssets[scene.MockupId];
                    var shot = aiPayload?.Storyboard.AiShots?.SingleOrDefault(item => item.SceneOrder == scene.SceneOrder);
                    var clip = shot is null ? null : aiPayload!.AiShotStates.SingleOrDefault(item =>
                        item.SceneOrder == shot.SceneOrder && item.Fingerprint == shot.Fingerprint && item.Status == "stored");
                    return new VideoSceneExportSource(scene.SceneOrder, scene.MockupId, scene.SourceRevision, scene.SourceHash,
                        source.StorageKey, source.StorageVersion, shot?.Fingerprint, clip?.StorageKey, clip?.StorageVersion);
                }).ToArray();
                snapshot["storyboard"] = JsonSerializer.SerializeToNode(payload.Storyboard, WorkflowJson.Options);
                snapshot["exportSources"] = JsonSerializer.SerializeToNode(new VideoExportSourceSnapshot(1, video.Id,
                    payload.Storyboard.Fingerprint, sceneSources), WorkflowJson.Options);
                video.ConfigSnapshot = snapshot.ToJsonString(WorkflowJson.Options);
            }
            video.MusicTrackId = payload.Storyboard.VideoEdit?.Music?.TrackId;
            video.Status = "completed"; video.StorageProvider = "cloudinary"; video.StorageKey = expected; video.StorageVersion = request.StorageVersion;
            video.ThumbnailStorageKey = thumb; video.ThumbnailStorageVersion = request.ThumbnailStorageVersion;
            video.QaResult = WorkflowJson.Write(new
            {
                technical = request.Qa,
                etsyCompliance = EtsyVideoCompliance.Evaluate(request.Qa!, payload.Storyboard.AspectRatio, "mp4")
            });
            video.FileSizeMb = request.Qa!.Bytes / (1024m * 1024m); video.UpdatedAt = time.GetUtcNow().UtcDateTime;
            queueAutomaticQuality = payload.Storyboard.Mode == "ai_shot";
            qualityVideoId = video.Id;
        }
        else
        {
            var payload = WorkflowJson.Read<ExportJobPayload>(job.Payload);
            var video = await videos.GetByIdAsync(payload.VideoId, ct);
            if (video?.ApprovalStatus != "approved") return Result.Failure(Error.Conflict("NotApproved", "Video approval was revoked."));
            foreach (var scene in await scenes.FindAsync(x => x.PromoVideoId == video.Id, ct))
            {
                var asset = scene.MockupImageId.HasValue ? await state.LockMockupAsync(scene.MockupImageId.Value, ct) : null;
                if (asset?.ApprovedRevision != scene.SourceRevision || asset?.MetadataRevision != scene.SourceRevision ||
                    asset?.ApprovalStatus != "approved" || asset?.ContentHash != scene.SourceHash)
                    return Result.Failure(Error.Conflict("SourceChanged", "Source mockup approval changed during export."));
            }
            var package = (await exports.GetByIdAsync(payload.ExportId, ct))!;
            package.Status = "ready"; package.StorageProvider = "local"; package.StorageKey = expected;
            package.FileSizeMb = (request.Qa?.Bytes ?? request.OutputBytes) / (1024m * 1024m);
            foreach (var item in await items.FindAsync(x => x.ExportPackageId == package.Id, ct)) item.ItemStatus = "packed";
        }
        job.Status = "succeeded"; job.Progress = 100; job.ErrorMessage = null; job.LeaseToken = null; job.LeaseExpiresAt = null;
        job.UpdatedAt = time.GetUtcNow().UtcDateTime;
        var node = (await nodes.GetByIdAsync(job.WorkflowNodeRunId, ct))!; node.Status = "succeeded"; node.Progress = 100; node.ErrorMessage = null;
        run!.Revision++; run.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
        if (queueAutomaticQuality && qualityJobs is not null)
            await qualityJobs.QueueAsync(job.WorkflowRunId, qualityVideoId, null, ct);
        await runtime.AdvanceAsync(run, ct); await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (job.Kind == "render" && sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        if (runNotifier != null) await runNotifier.RunChangedAsync(run, ct);
        return Result.Success();
    }
    private static JsonObject? ReadConfigSnapshot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return JsonNode.Parse(value) as JsonObject; }
        catch (JsonException) { return null; }
    }
    public async Task<Result> FailAsync(Guid id, JobFailure request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null) return Result.Failure(LeaseError());
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (!ValidLease(job, request.LeaseToken, run)) return Result.Failure(LeaseError());
        // The worker only names what went wrong; what the seller reads is written here, with the provider's reason when the scene has one.
        var reason = request.Message is "AiShotRejected" or "AiShotFailed" ? AiShotFailure(job!) : null;
        var note = request.Message switch
        {
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" => AiShotFailure(job!, "submitting"),
            "AiVideoBusy" => AiShotFailure(job!, "pending"),
            _ => null
        };
        // Over quota or a temporary provider error, which billed nothing: tried again once a per-minute limit has
        // had time to reset, and left to the seller when the job runs out of attempts.
        var busy = request.Message == "AiVideoBusy";
        job!.ErrorMessage = request.Message switch
        {
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" when note != null => $"AI video request status is uncertain: {note} Retry will not submit it again; choose Regenerate AI clips after checking provider usage.",
            "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" => "AI video request status is uncertain. Retry will not submit it again; choose Regenerate AI clips after checking provider usage.",
            "AiVideoBusy" => $"{note ?? "The AI video provider is busy or over quota."} Nothing was billed for it; choose Retry checkpoint once the quota allows it.",
            "SourceChanged" => "A source mock-up changed or is no longer approved. Approve the mock-ups again, then retry.",
            "SourceDownloadFailed" => "An export source could not be downloaded. Check that the approved design, mock-ups and video are still available, then retry.",
            "AiShotRejected" or "AiShotFailed" when reason != null =>
                $"The video provider did not generate this AI scene: {reason} Choose Retry checkpoint to submit it again; if it keeps refusing, change the source image or the motion direction.",
            "AiShotRejected" => "The video provider rejected this AI scene. Change its approved source or motion instruction, then regenerate AI clips.",
            "AiShotFailed" => "AI Shot could not finish. Review the approved source or prompt, then regenerate AI clips if needed.",
            _ => "Worker could not finish media processing. Retry or review source assets."
        };
        job.LeaseToken = null; job.LeaseExpiresAt = null;
        // Trying again by itself cannot change a refusal, a changed source or an uncertain submission.
        job.Status = request.Message is "VeoSubmissionUncertain" or "AiVideoSubmissionUncertain" or "AiShotRejected" or "SourceChanged" || reason != null || job.Attempt >= job.MaximumAttempts ? "failed" : "queued";
        job.AvailableAt = time.GetUtcNow().AddSeconds(busy ? 70 * job.Attempt : 5 * job.Attempt).UtcDateTime;
        // A failed render fails the run; a failed export fails only its output, beside the others still at work.
        if (job.Status == "failed") await runtime.FailOutputAsync(run!, job.WorkflowNodeRunId, job.ErrorMessage, ct);
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (job.Kind == "render" && sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        if (runNotifier != null) await runNotifier.RunChangedAsync(run!, ct);
        return Result.Success();
    }
    // What the AI scene in the given state recorded about itself: its provider's refusal, or why its outcome is unknown.
    private static string? AiShotFailure(MediaJob job, string status = "failed")
    {
        if (job.Kind != "render") return null;
        try
        {
            return WorkflowJson.Read<AiRenderJobPayload>(job.Payload).AiShotStates?
                .FirstOrDefault(x => x.Status == status && !string.IsNullOrWhiteSpace(x.Failure))?.Failure;
        }
        catch (JsonException) { return null; }
    }
    private bool ValidLease(MediaJob? job, Guid token, WorkflowRun? run) => job is { Status: "leased" } && job.LeaseToken == token &&
        job.LeaseExpiresAt > time.GetUtcNow().UtcDateTime && run?.Status == "running";
    private static Error LeaseError() => Error.Conflict("LeaseLost", "Job lease expired or run was cancelled.");
    private static string OutputKey(MediaJob job, Guid token, string suffix) => $"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/{token}/{suffix}";
    public static bool ValidQa(MediaQa? qa, double duration, int width = 1080, int height = 1920, bool expectAudio = false) => qa != null && qa.Codec == "h264" && qa.PixelFormat == "yuv420p" &&
        qa.Width == width && qa.Height == height && Math.Abs(qa.Fps - 30) < .01 && Math.Abs(qa.DurationSeconds - duration) <= 1d / 30 + .01 &&
        qa.Bytes is > 0 and < 100 * 1024 * 1024 && qa.HasAudio == expectAudio && qa.FullDecode;
}
