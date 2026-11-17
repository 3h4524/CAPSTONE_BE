using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Moderation;
using APCS.Application.Features.Moderation.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

/// <summary>Lease-bound request for processing an AI scene.</summary>
public sealed record AiShotLeaseRequest(Guid LeaseToken);
/// <summary>Lease-bound confirmation that a generated clip was uploaded to private storage.</summary>
public sealed record AiShotStoredRequest(Guid LeaseToken, string StorageKey, string StorageVersion);
/// <summary>Progress of a single external AI video operation.</summary>
public sealed record AiShotProgressResponse(string Status, string? Message = null);

public interface IAiShotJobService
{
    Task<Result<AiShotProgressResponse>> StartAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result<AiShotProgressResponse>> PollAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result<byte[]>> DownloadAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result> StoredAsync(Guid jobId, int sceneOrder, AiShotStoredRequest request, CancellationToken ct);

    /// <summary>Submits a scene of a job whose clips the API makes itself (<c>generating_clips</c>), with no worker lease.</summary>
    Task<Result<AiShotProgressResponse>> StartClipAsync(Guid jobId, int sceneOrder, CancellationToken ct);
    /// <summary>Asks the provider how far a scene of a <c>generating_clips</c> job is.</summary>
    Task<Result<AiShotProgressResponse>> PollClipAsync(Guid jobId, int sceneOrder, CancellationToken ct);
    /// <summary>Downloads a finished scene of a <c>generating_clips</c> job and keeps it in private storage.</summary>
    Task<Result> StoreClipAsync(Guid jobId, int sceneOrder, CancellationToken ct);
}

public sealed class AiShotJobService(IRepository<MediaJob> jobs, IRepository<MockupImage> assets,
    IAiVideoCredentialResolver videoCredentials, IWorkflowStateRepository state, IUnitOfWork uow,
    IMediaStorage storage, IVeoVideoProvider provider, TimeProvider time,
    IAiVideoGenerationStrategyFactory? providerFactory = null, ILogger<AiShotJobService>? logger = null,
    ISceneVideoNotifier? sceneNotifier = null, IContentModerationService? moderation = null,
    ISystemApiKeyPool? systemKeys = null) : IAiShotJobService
{
    /// <summary>How often one scene is sent again without asking, and only after its provider said the dropped clip was free.</summary>
    public const int MaximumResubmissions = 1;

    public Task<Result<AiShotProgressResponse>> StartAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct) =>
        StartCoreAsync(jobId, sceneOrder, request.LeaseToken, ct);

    public Task<Result<AiShotProgressResponse>> StartClipAsync(Guid jobId, int sceneOrder, CancellationToken ct) =>
        StartCoreAsync(jobId, sceneOrder, null, ct);

    /// <param name="token">The worker's lease, or null when the API makes the clips of a <c>generating_clips</c> job itself.</param>
    private async Task<Result<AiShotProgressResponse>> StartCoreAsync(Guid jobId, int sceneOrder, Guid? token, CancellationToken ct)
    {
        AiShotPlan shot;
        MediaJob job;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var loaded = await LoadAsync(jobId, sceneOrder, token, ct);
            if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
            (job, var payload, var item) = loaded.Value;
            if (item.Status == "stored") return Result.Success(new AiShotProgressResponse("stored"));
            if (item.OperationName != null) return Result.Success(new AiShotProgressResponse(item.Status));
            if (item.Status != "pending") return Result.Failure<AiShotProgressResponse>(Error.Conflict("AiVideoSubmissionUncertain", "The video provider may have received this request. It will not be sent again automatically."));
            shot = payload.Storyboard.AiShots!.Single(x => x.SceneOrder == sceneOrder);
            var scene = payload.Storyboard.Scenes.Single(x => x.SceneOrder == sceneOrder);
            var source = await assets.GetByIdAsync(shot.MockupId, ct);
            if (source is null || source.ContentHash != shot.SourceHash || source.ApprovalStatus != "approved" || source.MetadataRevision != scene.SourceRevision || source.ApprovedRevision != scene.SourceRevision)
                return Result.Failure<AiShotProgressResponse>(Error.Conflict("SourceChanged", "AI Shot source changed or is no longer approved."));
            // The immutable scene source itself is checked above by id, hash, revision and approval.
            // Re-checking the prompt, references or original image would reject a valid approved
            // keyframe after harmless workflow edits and could strand an already paid run.
            Replace(job, payload, item with { Status = "submitting" });
            await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        }
        // A provider the platform holds several keys for takes one of them that has some of today's limit left; the
        // use is given back below whenever the provider made nothing.
        ISystemApiKeyLease? lease = null;
        if (systemKeys?.HasKeys(shot.ProviderKey) == true)
        {
            try { lease = await systemKeys.AcquireAsync(shot.ProviderKey, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await MarkAsync(jobId, sceneOrder, token, "pending", null, CancellationToken.None);
                throw;
            }
            if (lease is null)
            {
                var providerName = ProviderName(shot.ProviderKey);
                var usedUp = $"{providerName} has made as many clips as the platform allows today. Try again tomorrow.";
                logger?.LogWarning("AI scene {Scene} of job {Job} found every system {Provider} key used up for today", sceneOrder, jobId, providerName);
                await MarkAsync(jobId, sceneOrder, token, "pending", usedUp, ct);
                return Result.Failure<AiShotProgressResponse>(Error.Failure("AiVideoDailyLimitReached", usedUp));
            }
        }
        await using var heldKey = lease;
        string key;
        try { key = lease?.ApiKey ?? await KeyAsync(job.UserId, shot.ProviderKey, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Nothing was sent: the scene is put back, or the next attempt would take it for a half-sent request.
            await MarkAsync(jobId, sceneOrder, token, "pending", null, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Nothing has been sent yet; the configured credential must be restored before a retry can work.
            logger?.LogWarning(ex, "AI scene {Scene} of job {Job} has no usable {Provider} key", sceneOrder, jobId, shot.ProviderKey);
            var missing = string.Equals(shot.ProviderKey, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase)
                ? "AI video is temporarily unavailable because the system MiniMax API key is not configured."
                : $"The connected {shot.ProviderKey} API key is missing or failed its last check. Reconnect it, then retry.";
            await MarkAsync(jobId, sceneOrder, token, "failed", missing, ct);
            return Result.Failure<AiShotProgressResponse>(Error.Validation(missing));
        }
        // The last check before a paid request leaves: the prompt was checked when it was planned, so this is almost
        // always a cached answer. Nothing has been sent, so a blocked scene is simply failed with the reason.
        if (moderation is not null)
        {
            var verdict = await moderation.CheckTextAsync(
                [new ModerationText($"the scene {sceneOrder + 1} video prompt", shot.Prompt)],
                new ModerationContext(job.UserId, ModerationFeatures.AiVideo, ModerationDirection.Input, ModerationSubjects.AiShot, jobId),
                ct);
            if (verdict.IsBlocked)
            {
                var blocked = ModerationPolicy.BlockedMessage(verdict);
                await MarkAsync(jobId, sceneOrder, token, "failed", blocked, ct);
                return Result.Failure<AiShotProgressResponse>(ModerationErrors.Blocked(verdict));
            }
        }

        string operation;
        try
        {
            var source = (await assets.GetByIdAsync(shot.MockupId, ct))!;
            operation = await ProviderFor(shot).StartAsync(key, storage.SignRead(source.StorageKey, "image", source.StorageVersion),
                shot.Prompt, ReadAspect(job.Payload), shot.DurationSeconds,
                shot.Preset is "lifestyle_motion" or "product_in_use" or "lifestyle_in_use" or "creator_pov_ugc" or "everyday_story_moment",
                shot.SourceCrop, ct);
        }
        // Every provider HTTP error below means it made no operation: nothing is being generated or billed, so
        // only a request whose outcome is really unknown (no answer, or an answer that cannot be read) is uncertain.
        catch (AiVideoProviderException ex) when (ex.StatusCode is 408 or 429 or >= 500)
        {
            // Over quota, overloaded or failing on the provider's side: the scene goes back to pending and the job's next
            // attempt may submit it again.
            var providerName = ProviderName(shot.ProviderKey);
            var errorCode = shot.ProviderKey == "gemini" ? "VeoBusy" : "AiVideoProviderBusy";
            var busy = ex.StatusCode switch
            {
                429 => $"{providerName} quota or rate limit reached. Check the provider account's usage, then try again.",
                503 => $"{providerName} is overloaded right now. Try again in a few minutes.",
                _ => $"{providerName} had a temporary error ({ex.StatusCode}) and made no clip. Try again in a few minutes."
            };
            logger?.LogWarning("AI scene {Scene} of job {Job} was refused by {Provider} with status {Status}: {Detail}", sceneOrder, jobId, providerName, ex.StatusCode, ex.Detail);
            if (lease is not null) await lease.RefundAsync(CancellationToken.None);
            await MarkAsync(jobId, sceneOrder, token, "pending", ex.Detail == null ? busy : $"{busy} {providerName}: {ex.Detail}", ct, token is null ? BusyDelay : null);
            return Result.Failure<AiShotProgressResponse>(Error.Failure(errorCode, busy));
        }
        catch (AiVideoProviderException ex)
        {
            var providerName = ProviderName(shot.ProviderKey);
            var refusal = ex.StatusCode is 401 or 403
                ? string.Equals(shot.ProviderKey, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase)
                    ? $"{providerName} access denied. Check the system MiniMax key, billing and model availability."
                    : $"{providerName} access denied. Check the connected {shot.ProviderKey} key, billing and model availability."
                : ex.StatusCode is 400 or 404
                    ? $"{providerName} rejected this source or prompt. Review the AI scene settings."
                    : $"{providerName} refused the request ({ex.StatusCode}). Review the AI scene settings.";
            // A platform key the provider no longer accepts is passed over for the rest of the day.
            if (lease is not null)
            {
                if (ex.StatusCode is 401 or 403) await lease.DisableForTodayAsync($"{providerName} returned {ex.StatusCode}: {ex.Detail}", CancellationToken.None);
                else await lease.RefundAsync(CancellationToken.None);
            }
            // Kept with the scene, so the run can show the seller what its provider said and not only that it refused.
            await MarkAsync(jobId, sceneOrder, token, "failed", ex.Detail == null ? refusal : $"{refusal} {providerName}: {ex.Detail}", ct);
            return Result.Failure<AiShotProgressResponse>(Error.Validation(refusal));
        }
        catch (AiVideoNotSubmittedException ex)
        {
            // Failed before the POST reached the provider, so the job's next attempt may submit it. Recorded even when the
            // worker has gone, or the scene would be left looking half-sent.
            logger?.LogWarning(ex, "AI scene {Scene} of job {Job} was not submitted", sceneOrder, jobId);
            if (lease is not null) await lease.RefundAsync(CancellationToken.None);
            await MarkAsync(jobId, sceneOrder, token, "pending", ex.Message, CancellationToken.None, token is null ? NotSubmittedDelay : null);
            return Result.Failure<AiShotProgressResponse>(Error.Failure("AiVideoNotSubmitted", ex.Message));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // The provider may have accepted a timed-out POST. Never re-submit automatically, but say what happened.
            var providerName = ProviderName(shot.ProviderKey);
            logger?.LogError(ex, "AI scene {Scene} of job {Job} has an unknown {Provider} submission result", sceneOrder, jobId, providerName);
            var unknown = ex is AiVideoSubmissionUnknownException ? ex.Message
                : ex is TaskCanceledException or TimeoutException ? $"{providerName} did not answer in time."
                : $"The request to {providerName} broke off: {ex.Message}";
            await MarkAsync(jobId, sceneOrder, token, "submitting", unknown, CancellationToken.None);
            return Result.Failure<AiShotProgressResponse>(Error.Conflict("AiVideoSubmissionUncertain", "AI video submission failed or has an unknown result. Do not auto-retry this paid request."));
        }
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var loaded = await LoadAsync(jobId, sceneOrder, token, ct);
            if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
            (job, var payload, var item) = loaded.Value;
            Replace(job, payload, item with { OperationName = operation, Status = "polling", KeyId = lease?.KeyId,
                SubmittedAt = time.GetUtcNow().UtcDateTime, NextAttemptAt = null });
            await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        }
        return Result.Success(new AiShotProgressResponse("polling"));
    }

    public Task<Result<AiShotProgressResponse>> PollAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct) =>
        PollCoreAsync(jobId, sceneOrder, request.LeaseToken, ct);

    public Task<Result<AiShotProgressResponse>> PollClipAsync(Guid jobId, int sceneOrder, CancellationToken ct) =>
        PollCoreAsync(jobId, sceneOrder, null, ct);

    private async Task<Result<AiShotProgressResponse>> PollCoreAsync(Guid jobId, int sceneOrder, Guid? token, CancellationToken ct)
    {
        var loaded = await LoadAsync(jobId, sceneOrder, token, ct);
        if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
        var (job, payload, item) = loaded.Value;
        if (item.Status is "stored" or "ready") return Result.Success(new AiShotProgressResponse(item.Status));
        if (item.OperationName == null) return Result.Failure<AiShotProgressResponse>(Error.Conflict("AiVideoSubmissionUncertain", "No recoverable AI video operation was recorded."));
        var shot = payload.Storyboard.AiShots!.Single(x => x.SceneOrder == sceneOrder);
        VeoOperationResult result;
        try { result = await ProviderFor(shot).PollAsync(await KeyAsync(job.UserId, shot.ProviderKey, ct, item.KeyId), item.OperationName, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Result.Failure<AiShotProgressResponse>(Error.Failure("AiVideoProviderUnavailable", "Could not check AI video progress. Retry the status check.")); }
        if (!result.Done) return Result.Success(new AiShotProgressResponse("polling"));
        await using var tx = await uow.BeginTransactionAsync(ct);
        loaded = await LoadAsync(jobId, sceneOrder, token, ct);
        if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
        (job, payload, item) = loaded.Value;
        var failure = result.Failure ?? "The video provider returned no video.";
        // A provider can drop a finished clip and explicitly say the attempt was free. That is worth one more submission
        // inside what the seller already confirmed; a second drop is left for them to decide.
        var resubmit = result.VideoUri == null && result.NotCharged && item.Resubmissions < MaximumResubmissions;
        var next = result.VideoUri != null ? item with { DownloadUri = result.VideoUri, Status = "ready", Failure = null }
            : resubmit ? item with { Status = "pending", OperationName = null, Failure = failure, Resubmissions = item.Resubmissions + 1 }
            : item with { Status = "failed", Failure = failure };
        Replace(job, payload, next);
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        return result.VideoUri != null ? Result.Success(new AiShotProgressResponse("ready"))
            : resubmit ? Result.Success(new AiShotProgressResponse("pending", failure))
            : Result.Failure<AiShotProgressResponse>(Error.Validation(failure));
    }

    public async Task<Result<byte[]>> DownloadAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct)
    {
        var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
        if (loaded == null) return Result.Failure<byte[]>(LeaseError());
        var (job, payload, item) = loaded.Value;
        if (item.Status != "ready" || item.DownloadUri == null) return Result.Failure<byte[]>(Error.Conflict("NotReady", "AI Shot clip is not ready."));
        var shot = payload.Storyboard.AiShots!.Single(x => x.SceneOrder == sceneOrder);
        try { return Result.Success(await ProviderFor(shot).DownloadAsync(await KeyAsync(job.UserId, shot.ProviderKey, ct, item.KeyId), item.DownloadUri, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Result.Failure<byte[]>(Error.Failure("AiVideoDownloadFailed", "Could not download the completed AI video clip.")); }
    }

    public async Task<Result> StoredAsync(Guid jobId, int sceneOrder, AiShotStoredRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
        if (loaded == null) return Result.Failure(LeaseError());
        var (job, payload, item) = loaded.Value;
        var expected = OutputKey(job, request.LeaseToken, sceneOrder);
        if (item.Status != "ready" || request.StorageKey != expected || request.StorageVersion.Length > 40 ||
            !await storage.VerifyAsync(expected, "video", request.StorageVersion, null, ct))
            return Result.Failure(Error.Validation("AI Shot upload could not be verified."));
        Replace(job, payload, item with { Status = "stored", StorageKey = expected, StorageVersion = request.StorageVersion, DownloadUri = null });
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        return Result.Success();
    }

    /// <summary>The largest clip kept: the worker refuses anything bigger, and so does the render.</summary>
    public const int MaximumClipBytes = 100 * 1024 * 1024;

    public async Task<Result> StoreClipAsync(Guid jobId, int sceneOrder, CancellationToken ct)
    {
        // Read what to fetch first, so the provider and storage are not called while the job and run are locked.
        AiShotPlan shot;
        MediaJob job;
        AiShotJobState item;
        {
            var loaded = await LoadAsync(jobId, sceneOrder, null, ct);
            if (loaded == null) return Result.Failure(LeaseError());
            (job, var payload, item) = loaded.Value;
            if (item.Status == "stored") return Result.Success();
            if (item.Status != "ready" || item.DownloadUri == null) return Result.Failure(Error.Conflict("NotReady", "AI Shot clip is not ready."));
            shot = payload.Storyboard.AiShots!.Single(x => x.SceneOrder == sceneOrder);
        }

        byte[] bytes;
        try { bytes = await ProviderFor(shot).DownloadAsync(await KeyAsync(job.UserId, shot.ProviderKey, ct, item.KeyId), item.DownloadUri!, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI scene {Scene} of job {Job} could not be downloaded", sceneOrder, jobId);
            return Result.Failure(Error.Failure("AiVideoDownloadFailed", "Could not download the completed AI video clip."));
        }
        if (bytes.Length == 0 || bytes.Length > MaximumClipBytes)
            return Result.Failure(Error.Failure("AiVideoDownloadFailed", "The completed AI video clip has an invalid size."));

        // A new key for every attempt: storage never overwrites, so a half-finished earlier upload must not be in the way.
        var key = OutputKey(job, Guid.NewGuid(), sceneOrder);
        string version;
        try
        {
            await using var content = new MemoryStream(bytes, writable: false);
            version = await storage.UploadVideoAsync(content, key, ct);
            if (version.Length > 40 || !await storage.VerifyAsync(key, "video", version, null, ct))
                return Result.Failure(Error.Failure("AiVideoStoreFailed", "The AI video clip could not be verified after it was stored."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "AI scene {Scene} of job {Job} could not be stored", sceneOrder, jobId);
            return Result.Failure(Error.Failure("AiVideoStoreFailed", "The AI video clip could not be stored."));
        }

        await using var tx = await uow.BeginTransactionAsync(ct);
        var current = await LoadAsync(jobId, sceneOrder, null, ct);
        if (current == null) return Result.Failure(LeaseError());
        var (lockedJob, lockedPayload, lockedItem) = current.Value;
        if (lockedItem.Status != "ready") return Result.Failure(Error.Conflict("NotReady", "AI Shot clip is not ready."));
        Replace(lockedJob, lockedPayload, lockedItem with { Status = "stored", StorageKey = key, StorageVersion = version, DownloadUri = null });
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(lockedJob.WorkflowRunId, ct);
        return Result.Success();
    }

    /// <summary>
    /// The scene with its job and run locked. A worker passes its lease token and the job must be leased to it; the API
    /// making the clips of a <c>generating_clips</c> job itself passes none. Either way the run must still be running.
    /// </summary>
    private async Task<(MediaJob Job, AiRenderJobPayload Payload, AiShotJobState State)?> LoadAsync(Guid id, int sceneOrder, Guid? token, CancellationToken ct)
    {
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null || unlocked.Kind != "render") return null;
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (job is null || run?.Status != "running") return null;
        var holdsIt = token is { } lease
            ? job.Status == "leased" && job.LeaseToken == lease && job.LeaseExpiresAt > time.GetUtcNow().UtcDateTime
            : job.Status == AiClipPhase.JobStatus;
        if (!holdsIt) return null;
        AiRenderJobPayload payload;
        try { payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload); }
        catch (System.Text.Json.JsonException) { return null; }
        var item = payload.AiShotStates.SingleOrDefault(x => x.SceneOrder == sceneOrder);
        return item == null ? null : (job, payload, item);
    }

    private static void Replace(MediaJob job, AiRenderJobPayload payload, AiShotJobState state) =>
        job.Payload = WorkflowJson.Write(payload with { AiShotStates = payload.AiShotStates.Select(x => x.SceneOrder == state.SceneOrder ? state : x).ToArray() });
    /// <summary>How long a scene the provider could not take for now waits, by how often that has happened.</summary>
    private static TimeSpan BusyDelay(int attempt) => TimeSpan.FromSeconds(70 * attempt);
    private static TimeSpan NotSubmittedDelay(int attempt) => TimeSpan.FromSeconds(5 * attempt);

    /// <param name="retryAfter">Counts this as one more time the provider could not take the scene, and says when it is sent again.</param>
    private async Task MarkAsync(Guid jobId, int sceneOrder, Guid? token, string status, string? failure, CancellationToken ct,
        Func<int, TimeSpan>? retryAfter = null)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var loaded = await LoadAsync(jobId, sceneOrder, token, ct);
        if (loaded == null) return;
        var (job, payload, item) = loaded.Value;
        var next = item with { Status = status, Failure = failure };
        if (retryAfter is not null)
        {
            var attempts = item.BusyAttempts + 1;
            next = next with { BusyAttempts = attempts, NextAttemptAt = time.GetUtcNow().UtcDateTime + retryAfter(attempts) };
        }
        Replace(job, payload, next);
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
    }
    private IVeoVideoProvider ProviderFor(AiShotPlan shot) => providerFactory?.Resolve(shot.ProviderKey, shot.ModelKey) ?? provider;

    // An operation started with a platform key is asked about with that same key: it belongs to that key's account.
    private async Task<string> KeyAsync(Guid owner, string providerKey, CancellationToken ct, string? keyId = null)
    {
        if (keyId is not null && systemKeys?.FindKey(providerKey, keyId) is { } started) return started;
        var key = await videoCredentials.ResolveAsync(owner, providerKey, ct);
        if (key == null) throw new InvalidOperationException($"Configured {providerKey} API key is missing.");
        return key;
    }
    private static string ReadAspect(string payload)
    {
        var board = WorkflowJson.Read<AiRenderJobPayload>(payload).Storyboard;
        return VideoOutputFormats.AiSourceAspect(board.OutputFormat);
    }
    public static string OutputKey(MediaJob job, Guid token, int sceneOrder) =>
        $"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/{token}/ai-shot-{sceneOrder}";
    private static Error LeaseError() => Error.Conflict("LeaseLost", "Media job lease expired or the run was cancelled.");
    private static string ProviderName(string providerKey) => providerKey.ToLowerInvariant() switch
    {
        "minimax" => "MiniMax",
        "gemini" => "Google Veo",
        _ => providerKey
    };
}
