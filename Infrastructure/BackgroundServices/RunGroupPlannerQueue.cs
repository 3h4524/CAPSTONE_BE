using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>An in-process wake-up for the run group planners; the planners also look on their own every minute.</summary>
public sealed class RunGroupPlannerQueue : IRunGroupPlannerQueue
{
    private readonly SemaphoreSlim _signal = new(0, RunGroupPlannerWorker.Concurrency);

    public void Signal()
    {
        // Wake every planner: each picks a different queued run.
        lock (_signal)
        {
            var missing = RunGroupPlannerWorker.Concurrency - _signal.CurrentCount;
            if (missing > 0) _signal.Release(missing);
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
