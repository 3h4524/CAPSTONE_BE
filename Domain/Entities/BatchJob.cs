using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a bulk product-generation job submitted by a user.
/// </summary>
public sealed class BatchJob : SoftDeletableEntity
{
    private readonly List<BatchJobProduct> _products = [];
    private readonly List<BatchJobLog> _logs = [];

    private BatchJob()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the job name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the job description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the uploaded source file type.
    /// </summary>
    public string SourceFileType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the uploaded source file URL.
    /// </summary>
    public string? SourceFileUrl { get; private set; }

    /// <summary>
    /// Gets the source file checksum, used to detect re-uploads of the same file.
    /// </summary>
    public string? SourceFileHash { get; private set; }

    /// <summary>
    /// Gets the job status.
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
    /// Gets the number of processed products.
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
    /// Gets the job configuration as JSON.
    /// </summary>
    public string Config { get; private set; } = "{}";

    /// <summary>
    /// Gets the queue priority.
    /// </summary>
    public string Priority { get; private set; } = "normal";

    /// <summary>
    /// Gets the estimated cost in USD.
    /// </summary>
    public decimal EstimatedCostUsd { get; private set; }

    /// <summary>
    /// Gets the actual cost in USD.
    /// </summary>
    public decimal ActualCostUsd { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when processing started.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when processing completed.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the projected UTC completion timestamp.
    /// </summary>
    public DateTimeOffset? EstimatedCompletionTimeUtc { get; private set; }

    /// <summary>
    /// Gets the products queued in this job.
    /// </summary>
    public IReadOnlyCollection<BatchJobProduct> Products => _products.AsReadOnly();

    /// <summary>
    /// Gets the job execution log.
    /// </summary>
    public IReadOnlyCollection<BatchJobLog> Logs => _logs.AsReadOnly();
}
