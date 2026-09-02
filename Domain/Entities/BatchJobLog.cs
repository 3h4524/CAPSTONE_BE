using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a diagnostic event emitted while processing a batch job.
/// </summary>
public sealed class BatchJobLog : CreationTrackedEntity
{
    private BatchJobLog()
    {
    }

    /// <summary>
    /// Gets the parent batch job identifier.
    /// </summary>
    public int BatchJobId { get; private set; }

    /// <summary>
    /// Gets the related batch item identifier, when applicable.
    /// </summary>
    public int? BatchJobProductId { get; private set; }

    /// <summary>
    /// Gets the log severity level.
    /// </summary>
    public string LogLevel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the event type.
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the human-readable log message.
    /// </summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Gets structured event details as JSON.
    /// </summary>
    public string? Details { get; private set; }

    /// <summary>
    /// Gets the measured event duration in milliseconds.
    /// </summary>
    public int? DurationMs { get; private set; }

    /// <summary>
    /// Gets the external API call identifier.
    /// </summary>
    public string? ApiCallIdentifier { get; private set; }

    /// <summary>
    /// Gets the parent batch job.
    /// </summary>
    public BatchJob BatchJob { get; private set; } = null!;

    /// <summary>
    /// Gets the related batch item, when applicable.
    /// </summary>
    public BatchJobProduct? BatchJobProduct { get; private set; }
}
