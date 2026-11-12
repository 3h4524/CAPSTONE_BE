namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Wakes the background planner of run groups. Planning a child run writes its listing and storyboard, which can
/// take seconds each, so confirming a group only queues its runs and signals here.
/// </summary>
public interface IRunGroupPlannerQueue
{
    /// <summary>Queued child runs are waiting.</summary>
    void Signal();

    /// <summary>Waits for a signal or the timeout; true when signalled.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
