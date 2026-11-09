namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Wakes the background workers that watch in-flight workflow work. While nothing is in flight they stop querying
/// the database, so a serverless database can scale to zero, and only a signal (or a long safety check) wakes them.
/// </summary>
public sealed class WorkflowActivitySignal
{
    /// <summary>
    /// How often an idle worker still looks once. Work is normally announced by a signal; this only catches work
    /// queued by another process (another API instance, a manual SQL change) that this process was not told about.
    /// </summary>
    public static readonly TimeSpan IdleSafetyCheck = TimeSpan.FromHours(6);

    /// <summary>
    /// Queued or leased jobs untouched for longer than this do not keep the workers watching. Without a running
    /// worker a queued job never moves, and it must not keep the database awake for ever.
    /// </summary>
    public static readonly TimeSpan InFlightWindow = TimeSpan.FromHours(2);

    /// <summary>A render or export job was committed as queued (raised by <c>MediaJobQueuedInterceptor</c>).</summary>
    public ActivitySignal Media { get; } = new();

    /// <summary>A publish step was put on the in-process publish queue.</summary>
    public ActivitySignal Publish { get; } = new();
}

/// <summary>
/// A notification every waiter sees. A waiter passes the version it read before it last looked for work, so a
/// notification raised while it was looking is not missed.
/// </summary>
public sealed class ActivitySignal
{
    private TaskCompletionSource next = NewSource();
    private long version;

    public long Version => Interlocked.Read(ref version);

    public void Notify()
    {
        Interlocked.Increment(ref version);
        Interlocked.Exchange(ref next, NewSource()).TrySetResult();
    }

    /// <summary>Waits until the version moves past <paramref name="seen"/> or the timeout ends; true when notified.</summary>
    public async Task<bool> WaitAsync(long seen, TimeSpan timeout, CancellationToken cancellationToken, TimeProvider? time = null)
    {
        // Read the source before the version: a notification after this read completes it, one before it moved the version.
        var pending = Volatile.Read(ref next);
        if (Version != seen)
            return true;
        try
        {
            await pending.Task.WaitAsync(timeout, time ?? TimeProvider.System, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
