using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a seller-owned batch processing job.
/// </summary>
public sealed class BatchJob : SoftDeletableEntity
{
    private readonly List<BatchJobProduct> _jobProducts = [];
    private readonly List<BatchJobLog> _logs = [];

    private BatchJob()
    {
    }

    /// <summary>
    /// Gets the identifier of the seller that owns the batch job.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the batch job name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional batch job description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the uploaded source file type.
    /// </summary>
    public string SourceFileType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the source file URL.
    /// </summary>
    public string? SourceFileUrl { get; private set; }

    /// <summary>
    /// Gets the source file content hash.
    /// </summary>
    public string? SourceFileHash { get; private set; }

    /// <summary>
    /// Gets the current processing status.
    /// </summary>
    public string Status { get; private set; } = "draft";

    /// <summary>
    /// Gets the completion percentage.
    /// </summary>
    public decimal ProgressPercentage { get; private set; }

    /// <summary>
    /// Gets the total number of products in the job.
    /// </summary>
    public int TotalProducts { get; private set; }

    /// <summary>
    /// Gets the number of successfully processed products.
    /// </summary>
    public int ProcessedProducts { get; private set; }

    /// <summary>
    /// Gets the number of failed products.
    /// </summary>
    public int FailedProducts { get; private set; }

    /// <summary>
    /// Gets the number of skipped products.
    /// </summary>
    public int SkippedProducts { get; private set; }

    /// <summary>
    /// Gets the batch configuration as JSON.
    /// </summary>
    public string Config { get; private set; } = "{}";

    /// <summary>
    /// Gets the processing priority.
    /// </summary>
    public string Priority { get; private set; } = "normal";

    /// <summary>
    /// Gets the UTC processing start timestamp.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC processing completion timestamp.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the estimated UTC completion timestamp.
    /// </summary>
    public DateTimeOffset? EstimatedCompletionTimeUtc { get; private set; }

    /// <summary>
    /// Gets the estimated processing cost in USD.
    /// </summary>
    public decimal EstimatedCostUsd { get; private set; }

    /// <summary>
    /// Gets the actual processing cost in USD.
    /// </summary>
    public decimal ActualCostUsd { get; private set; }

    /// <summary>
    /// Gets the products queued in this batch job.
    /// </summary>
    public IReadOnlyCollection<BatchJobProduct> JobProducts => _jobProducts.AsReadOnly();

    /// <summary>
    /// Gets the diagnostic log entries for this batch job.
    /// </summary>
    public IReadOnlyCollection<BatchJobLog> Logs => _logs.AsReadOnly();
}
