namespace APCS.Common.Constants;

/// <summary>
/// The lifecycle values stored in <c>BatchJob.Status</c>. Must stay within the DB CHECK constraint
/// <c>chk_batch_jobs_status</c>.
/// </summary>
public static class BatchJobStatuses
{
    /// <summary>Job row created so the Seller can attach mock-ups/prompt overrides before starting generation.</summary>
    public const string Draft = "draft";

    public const string Queued = "queued";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string PartiallyCompleted = "partially_completed";
    public const string Failed = "failed";

    /// <summary>Statuses that count as "already has an active job" for BR52.</summary>
    public static readonly string[] Active = [Queued, Running, Paused];
}
