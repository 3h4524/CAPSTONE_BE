namespace APCS.Common.Constants;

/// <summary>
/// The values of a batch's status. Must stay within the DB CHECK constraint <c>chk_batches_status</c>.
/// </summary>
public static class BatchStatuses
{
    /// <summary>Nothing is running and there are products left to run, or no products yet.</summary>
    public const string Draft = "draft";

    /// <summary>A job of the batch is queued or running.</summary>
    public const string Processing = "processing";

    /// <summary>Nothing is running and no product is left to run.</summary>
    public const string Completed = "completed";
}
