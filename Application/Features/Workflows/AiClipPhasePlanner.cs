namespace APCS.Application.Features.Workflows;

public enum AiClipAction { Submit, Poll, Store }

/// <summary>One call to make for one AI scene of a job.</summary>
public sealed record AiClipWork(Guid JobId, int SceneOrder, AiClipAction Action);

public enum AiClipSettlementKind { Complete, Fail }

/// <summary>A job whose AI scenes need nothing more from the API: it is ready for the worker, or it has failed.</summary>
/// <param name="Code">The failure code, in the words the worker reports (see <see cref="MediaJobFailures"/>), when the job failed.</param>
public sealed record AiClipSettlement(Guid JobId, AiClipSettlementKind Kind, string? Code = null);

/// <summary>A job whose scenes are being made, as the planner reads it.</summary>
public sealed record AiClipJob(Guid JobId, Guid UserId, IReadOnlyList<AiShotJobState> Scenes);

public sealed record AiClipPlan(IReadOnlyList<AiClipSettlement> Settlements, IReadOnlyList<AiClipWork> Work);

/// <summary>
/// Decides what the API does next with the AI scenes of the jobs in <c>generating_clips</c>: which jobs are finished or
/// failed, and which calls to make now. It only reads the states it is given, so every rule can be tried without a
/// database or a provider.
/// </summary>
public static class AiClipPhasePlanner
{
    public static AiClipPlan Plan(IReadOnlyList<AiClipJob> jobs, DateTime now, AiClipPhaseSettings settings)
    {
        var settlements = new List<AiClipSettlement>();
        var stores = new List<AiClipWork>();
        var polls = new List<AiClipWork>();
        // Scenes the provider is making now, which is what the limits count.
        var inFlight = 0;
        var inFlightByUser = new Dictionary<Guid, int>();
        var waiting = new List<(AiClipJob Job, AiShotJobState Scene)>();

        foreach (var job in jobs)
        {
            var failure = Failure(job.Scenes, now, settings);
            if (failure is not null) { settlements.Add(new(job.JobId, AiClipSettlementKind.Fail, failure)); continue; }
            if (job.Scenes.All(scene => scene.Status == "stored")) { settlements.Add(new(job.JobId, AiClipSettlementKind.Complete)); continue; }

            foreach (var scene in job.Scenes.OrderBy(scene => scene.SceneOrder))
            {
                switch (scene.Status)
                {
                    case "ready":
                        stores.Add(new(job.JobId, scene.SceneOrder, AiClipAction.Store));
                        break;
                    case "polling":
                        inFlight++;
                        inFlightByUser[job.UserId] = inFlightByUser.GetValueOrDefault(job.UserId) + 1;
                        if (DueForPoll(scene, now, settings)) polls.Add(new(job.JobId, scene.SceneOrder, AiClipAction.Poll));
                        break;
                    case "pending" when scene.NextAttemptAt is not { } next || next <= now:
                        waiting.Add((job, scene));
                        break;
                }
            }
        }

        return new(settlements, [.. stores, .. polls, .. Submissions(waiting, inFlight, inFlightByUser, settings)]);
    }

    /// <summary>Whether a scene the provider is making is asked about in this pass.</summary>
    private static bool DueForPoll(AiShotJobState scene, DateTime now, AiClipPhaseSettings settings)
    {
        // The provider called back to say it finished: asked at once, since the call itself is not trusted.
        if (scene.AnnouncedAt is not null) return true;
        if (scene.SubmittedAt is not { } submitted) return true;
        // The provider calls back when it is done: asking now and then only catches a call that never came.
        if (scene.Callback) return now - (scene.PolledAt ?? submitted) >= settings.CallbackPollInterval;
        // The provider takes minutes, so a scene is not asked about before it has had time to finish.
        return now - submitted >= settings.FirstPollDelay;
    }

    /// <summary>Why the job cannot go on, or null.</summary>
    private static string? Failure(IReadOnlyList<AiShotJobState> scenes, DateTime now, AiClipPhaseSettings settings)
    {
        // The provider turned the scene down for good (or its prompt was blocked): trying again cannot change it.
        if (scenes.Any(scene => scene.Status == "failed")) return "AiShotRejected";
        // A request that was started but whose outcome was never recorded may have been billed: never send it again.
        if (scenes.Any(scene => scene.Status == "submitting")) return "AiVideoSubmissionUncertain";
        // The provider kept turning the scene away (over quota, overloaded).
        if (scenes.Any(scene => scene.Status == "pending" && scene.BusyAttempts >= settings.MaxBusyAttempts)) return "AiVideoBusy";
        if (scenes.Any(scene => scene.Status is "polling" or "ready" && scene.SubmittedAt is { } submitted && now - submitted > settings.SceneTimeout))
            return "AiShotFailed";
        return null;
    }

    /// <summary>
    /// The scenes to submit, within the room the provider has left. Users take turns, the one with fewest scenes being made
    /// first, so one seller's batch does not fill every slot.
    /// </summary>
    private static IEnumerable<AiClipWork> Submissions(List<(AiClipJob Job, AiShotJobState Scene)> waiting, int inFlight,
        Dictionary<Guid, int> inFlightByUser, AiClipPhaseSettings settings)
    {
        var queues = waiting
            .GroupBy(item => item.Job.UserId)
            .Select(group => (UserId: group.Key, Scenes: new Queue<(AiClipJob Job, AiShotJobState Scene)>(group)))
            .ToList();
        var submitted = new List<AiClipWork>();
        while (inFlight < settings.MaxInFlight)
        {
            var next = queues
                .Where(queue => queue.Scenes.Count > 0 && inFlightByUser.GetValueOrDefault(queue.UserId) < settings.MaxInFlightPerUser)
                .OrderBy(queue => inFlightByUser.GetValueOrDefault(queue.UserId))
                .FirstOrDefault();
            if (next.Scenes is null) break;
            var (job, scene) = next.Scenes.Dequeue();
            submitted.Add(new(job.JobId, scene.SceneOrder, AiClipAction.Submit));
            inFlight++;
            inFlightByUser[next.UserId] = inFlightByUser.GetValueOrDefault(next.UserId) + 1;
        }
        return submitted;
    }
}
