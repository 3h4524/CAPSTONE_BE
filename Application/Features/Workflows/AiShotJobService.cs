using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Lease-bound request for processing an AI scene.</summary>
public sealed record AiShotLeaseRequest(Guid LeaseToken);
/// <summary>Lease-bound confirmation that a generated clip was uploaded to private storage.</summary>
public sealed record AiShotStoredRequest(Guid LeaseToken, string StorageKey, string StorageVersion);
/// <summary>Progress of a single external Veo operation.</summary>
public sealed record AiShotProgressResponse(string Status, string? Message = null);

public interface IAiShotJobService
{
    Task<Result<AiShotProgressResponse>> StartAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result<AiShotProgressResponse>> PollAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result<byte[]>> DownloadAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct);
    Task<Result> StoredAsync(Guid jobId, int sceneOrder, AiShotStoredRequest request, CancellationToken ct);
}

public sealed class AiShotJobService(IRepository<MediaJob> jobs, IRepository<MockupImage> assets,
    IApiKeyRepository keys, IApiKeyCredentials credentials, IWorkflowStateRepository state, IUnitOfWork uow,
    IMediaStorage storage, IVeoVideoProvider provider, TimeProvider time) : IAiShotJobService
{
    /// <summary>How often one scene is sent again without asking, and only after Google said the dropped clip was free.</summary>
    public const int MaximumResubmissions = 1;

    public async Task<Result<AiShotProgressResponse>> StartAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct)
    {
        AiShotPlan shot;
        MediaJob job;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
            if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
            (job, var payload, var item) = loaded.Value;
            if (item.Status == "stored") return Result.Success(new AiShotProgressResponse("stored"));
            if (item.OperationName != null) return Result.Success(new AiShotProgressResponse(item.Status));
            if (item.Status != "pending") return Result.Failure<AiShotProgressResponse>(Error.Conflict("VeoSubmissionUncertain", "Veo may have received this request. It will not be sent again automatically."));
            shot = payload.Storyboard.AiShots!.Single(x => x.SceneOrder == sceneOrder);
            var scene = payload.Storyboard.Scenes.Single(x => x.SceneOrder == sceneOrder);
            var source = await assets.GetByIdAsync(shot.MockupId, ct);
            if (source?.ContentHash != shot.SourceHash || source.ApprovalStatus != "approved" || source.MetadataRevision != scene.SourceRevision || source.ApprovedRevision != scene.SourceRevision)
                return Result.Failure<AiShotProgressResponse>(Error.Conflict("SourceChanged", "AI Shot source changed or is no longer approved."));
            Replace(job, payload, item with { Status = "submitting" });
            await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        string operation;
        try
        {
            var key = await KeyAsync(job.UserId, ct);
            var source = (await assets.GetByIdAsync(shot.MockupId, ct))!;
            operation = await provider.StartAsync(key, storage.SignRead(source.StorageKey, "image", source.StorageVersion),
                shot.Prompt, ReadAspect(job.Payload), shot.DurationSeconds,
                shot.Preset is "lifestyle_motion" or "product_in_use", ct);
        }
        catch (VeoProviderException ex) when (ex.StatusCode is 400 or 401 or 403 or 404)
        {
            var refusal = ex.StatusCode is 401 or 403
                ? "Google Veo access denied. Check the connected Gemini key, billing and model availability."
                : "Google Veo rejected this source or prompt. Review the AI Shot settings.";
            // Kept with the scene, so the run can show the seller what Google said and not only that it refused.
            await MarkAsync(jobId, sceneOrder, request.LeaseToken, "failed", ex.Detail == null ? refusal : $"{refusal} Google: {ex.Detail}", ct);
            return Result.Failure<AiShotProgressResponse>(Error.Validation(refusal));
        }
        catch (VeoProviderException ex) when (ex.StatusCode is 429 or 503)
        {
            // Google answered that it is over quota or overloaded: it took nothing and charged nothing, so the
            // scene goes back to pending and the job's next attempt may submit it again.
            await MarkAsync(jobId, sceneOrder, request.LeaseToken, "pending", null, ct);
            return Result.Failure<AiShotProgressResponse>(Error.Failure("VeoBusy", ex.StatusCode == 429
                ? "Google Veo quota or rate limit reached. Check the Gemini key's usage, then try again."
                : "Google Veo is overloaded right now. Try again in a few minutes."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // The provider may have accepted a timed-out POST. Never re-submit automatically.
            return Result.Failure<AiShotProgressResponse>(Error.Conflict("VeoSubmissionUncertain", "Veo submission failed or has an unknown result. Do not auto-retry this paid request."));
        }
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
            if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
            (job, var payload, var item) = loaded.Value;
            Replace(job, payload, item with { OperationName = operation, Status = "polling" });
            await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        return Result.Success(new AiShotProgressResponse("polling"));
    }

    public async Task<Result<AiShotProgressResponse>> PollAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct)
    {
        var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
        if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
        var (job, _, item) = loaded.Value;
        if (item.Status is "stored" or "ready") return Result.Success(new AiShotProgressResponse(item.Status));
        if (item.OperationName == null) return Result.Failure<AiShotProgressResponse>(Error.Conflict("VeoSubmissionUncertain", "No recoverable Veo operation was recorded."));
        VeoOperationResult result;
        try { result = await provider.PollAsync(await KeyAsync(job.UserId, ct), item.OperationName, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Result.Failure<AiShotProgressResponse>(Error.Failure("VeoUnavailable", "Could not check Veo progress. Retry the status check.")); }
        if (!result.Done) return Result.Success(new AiShotProgressResponse("polling"));
        await using var tx = await uow.BeginTransactionAsync(ct);
        loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
        if (loaded == null) return Result.Failure<AiShotProgressResponse>(LeaseError());
        (job, var payload, item) = loaded.Value;
        var failure = result.Failure ?? "Veo returned no video.";
        // Google sometimes drops a finished clip and says the attempt was free. That is worth one more submission
        // inside what the seller already confirmed; a second drop is left for them to decide.
        var resubmit = result.VideoUri == null && result.NotCharged && item.Resubmissions < MaximumResubmissions;
        var next = result.VideoUri != null ? item with { DownloadUri = result.VideoUri, Status = "ready", Failure = null }
            : resubmit ? item with { Status = "pending", OperationName = null, Failure = failure, Resubmissions = item.Resubmissions + 1 }
            : item with { Status = "failed", Failure = failure };
        Replace(job, payload, next);
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result.VideoUri != null ? Result.Success(new AiShotProgressResponse("ready"))
            : resubmit ? Result.Success(new AiShotProgressResponse("pending", failure))
            : Result.Failure<AiShotProgressResponse>(Error.Validation(failure));
    }

    public async Task<Result<byte[]>> DownloadAsync(Guid jobId, int sceneOrder, AiShotLeaseRequest request, CancellationToken ct)
    {
        var loaded = await LoadAsync(jobId, sceneOrder, request.LeaseToken, ct);
        if (loaded == null) return Result.Failure<byte[]>(LeaseError());
        var (job, _, item) = loaded.Value;
        if (item.Status != "ready" || item.DownloadUri == null) return Result.Failure<byte[]>(Error.Conflict("NotReady", "AI Shot clip is not ready."));
        try { return Result.Success(await provider.DownloadAsync(await KeyAsync(job.UserId, ct), item.DownloadUri, ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return Result.Failure<byte[]>(Error.Failure("VeoDownloadFailed", "Could not download the completed Veo clip.")); }
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
        return Result.Success();
    }

    private async Task<(MediaJob Job, AiRenderJobPayload Payload, AiShotJobState State)?> LoadAsync(Guid id, int sceneOrder, Guid token, CancellationToken ct)
    {
        var unlocked = await jobs.GetByIdAsync(id, ct);
        if (unlocked == null || unlocked.Kind != "render") return null;
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(id, ct);
        if (job?.Status != "leased" || job.LeaseToken != token || job.LeaseExpiresAt <= time.GetUtcNow().UtcDateTime || run?.Status != "running") return null;
        AiRenderJobPayload payload;
        try { payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload); }
        catch (System.Text.Json.JsonException) { return null; }
        var item = payload.AiShotStates.SingleOrDefault(x => x.SceneOrder == sceneOrder);
        return item == null ? null : (job, payload, item);
    }

    private static void Replace(MediaJob job, AiRenderJobPayload payload, AiShotJobState state) =>
        job.Payload = WorkflowJson.Write(payload with { AiShotStates = payload.AiShotStates.Select(x => x.SceneOrder == state.SceneOrder ? state : x).ToArray() });
    private async Task MarkAsync(Guid jobId, int sceneOrder, Guid token, string status, string? failure, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var loaded = await LoadAsync(jobId, sceneOrder, token, ct);
        if (loaded == null) return;
        var (job, payload, item) = loaded.Value;
        Replace(job, payload, item with { Status = status, Failure = failure });
        await uow.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task<string> KeyAsync(Guid owner, CancellationToken ct)
    {
        var key = (await keys.ListOwnedAsync(owner, ct)).FirstOrDefault(x => x.ServiceProvider == "gemini" && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key == null) throw new InvalidOperationException("Connected Gemini API key is missing.");
        return credentials.Unprotect(key.KeyValueEncrypted);
    }
    private static string ReadAspect(string payload)
    {
        var board = WorkflowJson.Read<AiRenderJobPayload>(payload).Storyboard;
        return board.OutputFormat == "landscape" ? "16:9" : "9:16";
    }
    public static string OutputKey(MediaJob job, Guid token, int sceneOrder) =>
        $"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/{token}/ai-shot-{sceneOrder}";
    private static Error LeaseError() => Error.Conflict("LeaseLost", "Media job lease expired or the run was cancelled.");
}
