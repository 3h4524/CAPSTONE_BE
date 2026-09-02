using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a timestamped system metric measurement.
/// </summary>
public sealed class SystemMetric : CreationTrackedEntity
{
    private SystemMetric()
    {
    }

    /// <summary>
    /// Gets the metric type.
    /// </summary>
    public string MetricType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp associated with the measurement.
    /// </summary>
    public DateTimeOffset MetricTimestampUtc { get; private set; }

    /// <summary>
    /// Gets the measured value.
    /// </summary>
    public decimal Value { get; private set; }

    /// <summary>
    /// Gets optional metric dimensions as JSON.
    /// </summary>
    public string? Dimension { get; private set; }
}
