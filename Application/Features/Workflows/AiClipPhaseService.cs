using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Workflows;

/// <summary>What one pass found: the calls to make now, and whether any job is still being looked after.</summary>
public sealed record AiClipPhasePlan(IReadOnlyList<AiClipWork> Work, bool HasJobs);

/// <summary>
/// Makes the AI scenes of render jobs in <c>generating_clips</c> in the API, so the media worker only ever gets a job whose
/// clips are already stored and spends its time rendering. A background pass calls <see cref="PlanAsync"/>, then
/// <see cref="RunAsync"/> for each piece of work, each in a scope of its own: one scope is not used by two calls at once.
/// </summary>
public interface IAiClipPhaseService
{
    /// <summary>Settles the jobs that are done, failed or cancelled and says which calls to make next.</summary>
    Task<AiClipPhasePlan> PlanAsync(CancellationToken ct);

    /// <summary>Makes one call for one scene and records the outcome.</summary>
    Task RunAsync(AiClipWork work, CancellationToken ct);
}

public sealed class AiClipPhaseService(IWorkflowStateRepository state, IUnitOfWork uow, IAiShotJobService shots,
    IRepository<MediaJob> jobs, IRepository<WorkflowNodeRun> nodes, WorkflowRuntime runtime,
    IOptions<AiClipPhaseSettings> options, TimeProvider time,
    ISceneVideoNotifier? sceneNotifier = null, IWorkflowRunNotifier? runNotifier = null,
    ILogger<AiClipPhaseService>? logger = null) : IAiClipPhaseService
{
    private AiClipPhaseSettings Settings => options.Value;

    public async Task<AiClipPhasePlan> PlanAsync(CancellationToken ct)
    {
        var found = await state.FindClipPhaseJobsAsync(Settings.MaxJobsPerPass, ct);
        if (found.Count == 0) return new([], false);

        var planned = new List<AiClipJob>();
        foreach (var job in found)
        {
            // A cancelled or failed run leaves its job here, and nobody else would ever look at it again.
            if (job.RunStatus != "running") { await CancelAsync(job.JobId, ct); continue; }
            try
            {
                var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
                planned.Add(new(job.JobId, job.UserId, payload.AiShotStates ?? []));
            }
            catch (JsonException)
            {
                logger?.LogError("Render job {Job} in generating_clips has no readable AI scene state", job.JobId);
                await FailAsync(job.JobId, "MediaProcessingFailed", ct);
            }
        }

        var plan = AiClipPhasePlanner.Plan(planned, time.GetUtcNow().UtcDateTime, Settings);
        foreach (var settlement in plan.Settlements)
        {
            if (settlement.Kind == AiClipSettlementKind.Complete) await FinishIfDoneAsync(settlement.JobId, ct);
            else await FailAsync(settlement.JobId, settlement.Code!, ct);
        }
        return new(plan.Work, true);
    }

    public async Task RunAsync(AiClipWork work, CancellationToken ct)
    {
        switch (work.Action)
        {
            case AiClipAction.Submit:
                await SubmitAsync(work, ct);
                break;
            case AiClipAction.Poll:
                await PollAsync(work, ct);
                break;
            case AiClipAction.Store:
                await StoreAsync(work, ct);
                break;
        }
    }

    private async Task SubmitAsync(AiClipWork work, CancellationToken ct)
    {
        var result = await shots.StartClipAsync(work.JobId, work.SceneOrder, ct);
        if (result.IsSuccess) return;
        switch (result.Error.Code)
        {
            // The scene was put back with a time to try again, or the run was cancelled: nothing more to do now.
            case "VeoBusy" or "AiVideoProviderBusy" or "AiVideoNotSubmitted" or "LeaseLost":
                return;
            case "AiVideoSubmissionUncertain" or "SourceChanged":
                await FailAsync(work.JobId, result.Error.Code, ct);
                return;
            // Every platform key is used up for today: nothing will be made until tomorrow.
            case "AiVideoDailyLimitReached":
                await FailAsync(work.JobId, "AiVideoBusy", ct);
                return;
            // A refusal, a blocked prompt or a missing key: the scene is marked failed, which the next pass settles.
            default:
                logger?.LogWarning("AI scene {Scene} of job {Job} was not submitted: {Code}", work.SceneOrder, work.JobId, result.Error.Code);
                return;
        }
    }

    private async Task PollAsync(AiClipWork work, CancellationToken ct)
    {
        var result = await shots.PollClipAsync(work.JobId, work.SceneOrder, ct);
        if (result.IsSuccess)
        {
            // Fetch a finished clip at once instead of waiting for the next pass.
            if (result.Value.Status == "ready") await StoreAsync(work with { Action = AiClipAction.Store }, ct);
            return;
        }
        // The provider could not be reached: asked again on the next pass, and the time limit ends it if it never answers.
        // A scene the provider finished without a clip is marked failed, which the next pass settles.
        if (result.Error.Code == "AiVideoSubmissionUncertain") await FailAsync(work.JobId, result.Error.Code, ct);
    }

    private async Task StoreAsync(AiClipWork work, CancellationToken ct)
    {
        var result = await shots.StoreClipAsync(work.JobId, work.SceneOrder, ct);
        if (result.IsSuccess) await FinishIfDoneAsync(work.JobId, ct);
        else if (result.Error.Code != "LeaseLost")
            logger?.LogWarning("AI scene {Scene} of job {Job} could not be stored yet: {Code}", work.SceneOrder, work.JobId, result.Error.Code);
    }

    /// <summary>
    /// When every scene is stored, hands the job to the media worker: it is queued, which the media queue announces to the
    /// worker. Otherwise only the progress moves on.
    /// </summary>
    private async Task FinishIfDoneAsync(Guid jobId, CancellationToken ct)
    {
        WorkflowRun? run;
        MediaJob job;
        int progress;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var held = await LockAsync(jobId, ct);
            if (held is null) return;
            (run, job) = held.Value;
            AiRenderJobPayload payload;
            try { payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload); }
            catch (JsonException) { return; }
            var scenes = payload.AiShotStates ?? [];
            var stored = scenes.Count(scene => scene.Status == "stored");
            var now = time.GetUtcNow().UtcDateTime;
            var done = stored == scenes.Count;
            progress = done ? AiClipPhase.DoneProgress : Math.Max(job.Progress, AiClipPhase.StartProgress + (int)Math.Round(20d * stored / Math.Max(1, scenes.Count)));
            if (!done && progress == job.Progress) return;
            job.Progress = progress; job.Stage = AiClipPhase.Stage; job.UpdatedAt = now;
            if (done) { job.Status = "queued"; job.AvailableAt = now; }
            var node = await nodes.GetByIdAsync(job.WorkflowNodeRunId, ct);
            if (node is not null) { node.Progress = progress; node.Stage = AiClipPhase.Stage; }
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        if (runNotifier != null) await runNotifier.ProgressAsync(job.WorkflowRunId, run!.RunGroupId, AiClipPhase.Stage, progress, ct, "generate-video");
    }

    private async Task FailAsync(Guid jobId, string code, CancellationToken ct)
    {
        WorkflowRun? run;
        MediaJob job;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var held = await LockAsync(jobId, ct);
            if (held is null) return;
            (run, job) = held.Value;
            var failure = MediaJobFailures.Describe(code, job);
            job.ErrorMessage = failure.Message;
            job.Status = "failed"; job.LeaseToken = null; job.LeaseExpiresAt = null; job.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await runtime.FailAsync(run!, job.WorkflowNodeRunId, failure.Message, ct);
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        logger?.LogWarning("Render job {Job} failed while its AI scenes were made: {Code}", jobId, code);
        if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.WorkflowRunId, ct);
        if (runNotifier != null) await runNotifier.RunChangedAsync(run!, ct);
    }

    private async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var unlocked = await jobs.GetByIdAsync(jobId, ct);
        if (unlocked is null) return;
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(jobId, ct);
        // Another pass or a cancel may have settled it since it was read.
        if (job is not { Status: AiClipPhase.JobStatus } || run?.Status == "running") return;
        job.Status = "cancelled"; job.LeaseToken = null; job.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>The run and the job, locked in that order as everywhere else, when the job is still generating its clips for a running run.</summary>
    private async Task<(WorkflowRun? Run, MediaJob Job)?> LockAsync(Guid jobId, CancellationToken ct)
    {
        var unlocked = await jobs.GetByIdAsync(jobId, ct);
        if (unlocked is null) return null;
        var run = await state.LockRunAsync(unlocked.WorkflowRunId, ct);
        var job = await state.LockJobAsync(jobId, ct);
        return job is { Status: AiClipPhase.JobStatus } && run?.Status == "running" ? (run, job) : null;
    }
}
