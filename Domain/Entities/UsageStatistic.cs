using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Aggregates a user's resource consumption for one billing period.
/// </summary>
public sealed class UsageStatistic : AuditableEntity
{
    private UsageStatistic()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the first day of the billing period.
    /// </summary>
    public DateOnly BillingPeriodStart { get; private set; }

    /// <summary>
    /// Gets the last day of the billing period.
    /// </summary>
    public DateOnly BillingPeriodEnd { get; private set; }

    /// <summary>
    /// Gets the number of images generated.
    /// </summary>
    public int ImagesGenerated { get; private set; }

    /// <summary>
    /// Gets the number of videos created.
    /// </summary>
    public int VideosCreated { get; private set; }

    /// <summary>
    /// Gets the number of listings exported.
    /// </summary>
    public int ListingsExported { get; private set; }

    /// <summary>
    /// Gets the total number of API calls.
    /// </summary>
    public int TotalApiCalls { get; private set; }

    /// <summary>
    /// Gets the total API cost in USD.
    /// </summary>
    public decimal TotalApiCostUsd { get; private set; }

    /// <summary>
    /// Gets the storage consumed in gigabytes.
    /// </summary>
    public decimal StorageUsedGb { get; private set; }

    /// <summary>
    /// Gets the number of completed batch jobs.
    /// </summary>
    public int BatchJobsCompleted { get; private set; }

    /// <summary>
    /// Gets the average processing duration in seconds.
    /// </summary>
    public decimal AverageProcessingTimeSeconds { get; private set; }
}
