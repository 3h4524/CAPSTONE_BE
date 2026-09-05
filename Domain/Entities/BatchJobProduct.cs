using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one product slot inside a batch job.
/// </summary>
public sealed class BatchJobProduct : AuditableEntity
{
    private BatchJobProduct()
    {
    }

    /// <summary>
    /// Gets the owning batch job identifier.
    /// </summary>
    public Guid BatchJobId { get; private set; }

    /// <summary>
    /// Gets the generated product identifier, once the row has been materialized.
    /// </summary>
    public Guid? ProductId { get; private set; }

    /// <summary>
    /// Gets the processing order within the job.
    /// </summary>
    public int SequenceOrder { get; private set; }

    /// <summary>
    /// Gets the originating row index in the uploaded file.
    /// </summary>
    public int? SourceRowIndex { get; private set; }

    /// <summary>
    /// Gets the raw uploaded row as JSON, kept for troubleshooting.
    /// </summary>
    public string? RawRowData { get; private set; }

    /// <summary>
    /// Gets the processing status.
    /// </summary>
    public string Status { get; private set; } = "pending";

    /// <summary>
    /// Gets the pipeline step currently running.
    /// </summary>
    public string? CurrentStep { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when processing started.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when processing completed.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the processing duration in seconds.
    /// </summary>
    public decimal? DurationSeconds { get; private set; }

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the owning batch job.
    /// </summary>
    public BatchJob BatchJob { get; private set; } = null!;

    /// <summary>
    /// Gets the generated product, when available.
    /// </summary>
    public Product? Product { get; private set; }
}
