namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Wakes the market schedule workers when a schedule is saved. Between saves they sleep until the next run is due
/// instead of reading the schedules every minute.
/// </summary>
public sealed class MarketScheduleSignal
{
    /// <summary>
    /// How long a worker sleeps at most. A schedule saved by another API instance is only seen when this worker next
    /// reads the schedules, so the wait is bounded.
    /// </summary>
    public static readonly TimeSpan LongestSleep = TimeSpan.FromHours(1);

    /// <summary>After a failed pass, how soon the worker tries again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>A seller saved a background market insight schedule.</summary>
    public ActivitySignal Insights { get; } = new();

    /// <summary>An admin saved the market research schedule.</summary>
    public ActivitySignal Research { get; } = new();

    /// <summary>How long to sleep from <paramref name="now"/> until <paramref name="dueAt"/>, within the bounds.</summary>
    public static TimeSpan Until(DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (dueAt is not { } due) return LongestSleep;
        var wait = due - now;
        if (wait < TimeSpan.FromSeconds(1)) return TimeSpan.FromSeconds(1);
        return wait > LongestSleep ? LongestSleep : wait;
    }
}
