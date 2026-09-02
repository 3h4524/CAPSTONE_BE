using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one product-processing item within a batch job.
/// </summary>
public sealed class BatchJobProduct : AuditableEntity
{
    private readonly List<BatchJobLog> _logs = [];

    private BatchJobProduct()
    {
    }

    /// <summary>
    /// Gets the parent batch job identifier.
    /// </summary>
    public int BatchJobId { get; private set; }

    /// <summary>
    /// Gets the generated product identifier, when available.
    /// </summary>
    public int? ProductId { get; private set; }

    /// <summary>
    /// Gets the item's order within the batch.
    /// </summary>
    public int SequenceOrder { get; private set; }

    /// <summary>
    /// Gets the original source row index.
    /// </summary>
    public int? SourceRowIndex { get; private set; }

    /// <summary>
    /// Gets the processing status.
    /// </summary>
    public string Status { get; private set; } = "pending";

    /// <summary>
    /// Gets the UTC processing start timestamp.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC processing completion timestamp.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the processing duration in seconds.
    /// </summary>
    public decimal? DurationSeconds { get; private set; }

    /// <summary>
    /// Gets the last processing error message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the parent batch job.
    /// </summary>
    public BatchJob BatchJob { get; private set; } = null!;

    /// <summary>
    /// Gets the generated product, when available.
    /// </summary>
    public Product? Product { get; private set; }

    /// <summary>
    /// Gets the log entries associated with this item.
    /// </summary>
    public IReadOnlyCollection<BatchJobLog> Logs => _logs.AsReadOnly();
}
