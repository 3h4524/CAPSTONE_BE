using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records a single event raised while a batch job runs.
/// </summary>
public sealed class BatchJobLog : CreationTrackedEntity
{
    private BatchJobLog()
    {
    }

    /// <summary>
    /// Gets the owning batch job identifier.
    /// </summary>
    public Guid BatchJobId { get; private set; }

    /// <summary>
    /// Gets the related product slot identifier, when the event is product-specific.
    /// </summary>
    public Guid? BatchJobProductId { get; private set; }

    /// <summary>
    /// Gets the related API usage record, when the event describes a provider call.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the log severity.
    /// </summary>
    public string LogLevel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the event type.
    /// </summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the log message.
    /// </summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Gets structured event details as JSON.
    /// </summary>
    public string? Details { get; private set; }

    /// <summary>
    /// Gets the event duration in milliseconds.
    /// </summary>
    public int? DurationMs { get; private set; }

    /// <summary>
    /// Gets the owning batch job.
    /// </summary>
    public BatchJob BatchJob { get; private set; } = null!;

    /// <summary>
    /// Gets the related product slot, when applicable.
    /// </summary>
    public BatchJobProduct? BatchJobProduct { get; private set; }

    /// <summary>
    /// Gets the related API usage record, when applicable.
    /// </summary>
    public ApiUsageRecord? ApiUsageRecord { get; private set; }
}
