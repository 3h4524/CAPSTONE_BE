using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Aggregates seller usage for one billing period.
/// </summary>
public sealed class UsageStatistic : AuditableEntity
{
    private UsageStatistic()
    {
    }

    /// <summary>
    /// Gets the owning seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the billing period start date.
    /// </summary>
    public DateOnly BillingPeriodStart { get; private set; }

    /// <summary>
    /// Gets the billing period end date.
    /// </summary>
    public DateOnly BillingPeriodEnd { get; private set; }

    /// <summary>
    /// Gets the number of generated images.
    /// </summary>
    public int ImagesGenerated { get; private set; }

    /// <summary>
    /// Gets the number of created videos.
    /// </summary>
    public int VideosCreated { get; private set; }

    /// <summary>
    /// Gets the number of exported listings.
    /// </summary>
    public int ListingsExported { get; private set; }

    /// <summary>
    /// Gets the total number of external API calls.
    /// </summary>
    public int TotalApiCalls { get; private set; }

    /// <summary>
    /// Gets the total external API cost in USD.
    /// </summary>
    public decimal TotalApiCostUsd { get; private set; }

    /// <summary>
    /// Gets the used storage in gigabytes.
    /// </summary>
    public decimal StorageUsedGb { get; private set; }

    /// <summary>
    /// Gets the number of completed batch jobs.
    /// </summary>
    public int BatchJobsCompleted { get; private set; }

    /// <summary>
    /// Gets the average product processing duration in seconds.
    /// </summary>
    public decimal AverageProcessingTimeSeconds { get; private set; }
}
