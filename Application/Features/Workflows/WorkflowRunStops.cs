using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>What stopping a run and going on with it later change: shared by a single run and the runs of a batch run.</summary>
public static class WorkflowRunStops
{
    /// <summary>
    /// Marks a video a stopped run was still making as failed, the status of a video that was not made: it is not
    /// counted against the plan, and going on with unchanged settings makes it again under the same version, keeping the
    /// AI clips already stored for it.
    /// </summary>
    /// <param name="generateEnded">
    /// Generate Video had finished before the stop. A video that is ready to be shown was finished by it, and one whose final
    /// render was under way goes back to that: the render is cancelled with the run and asked for again when the run goes on.
    /// </param>
    public static async Task EndUnfinishedVideosAsync(IRepository<PromoVideo> videos, Guid runId, CancellationToken ct, bool generateEnded = false)
    {
        foreach (var video in await videos.FindAsync(x => x.WorkflowRunId == runId && x.Status != "completed" && x.Status != "failed", ct))
        {
            if (video.Status == DeferredRender.VideoReady) continue;
            video.Status = video.Status == DeferredRender.VideoRendering && generateEnded ? DeferredRender.VideoReady : "failed";
        }
    }

    /// <summary>
    /// Opens the steps a stop left cancelled again, so the run carries on from the step it stopped at. The steps it had
    /// finished stay finished, and the caller sets the run's status (running, or queued for a batch run's planner).
    /// </summary>
    public static void Reopen(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> steps, DateTime now)
    {
        foreach (var step in steps.Where(x => x.Status == "cancelled"))
        {
            step.Status = "pending"; step.ErrorMessage = null; step.Stage = null; step.Progress = 0; step.UpdatedAt = now;
        }
        run.ErrorMessage = null; run.CompletedAt = null; run.UpdatedAt = now;
    }
}
