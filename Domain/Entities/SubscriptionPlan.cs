using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Defines a purchasable subscription tier and its quotas.
/// </summary>
public sealed class SubscriptionPlan : AuditableEntity
{
    private readonly List<Subscription> _subscriptions = [];

    private SubscriptionPlan()
    {
    }

    /// <summary>
    /// Gets the plan name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the unique plan tier.
    /// </summary>
    public string Tier { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the plan description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the monthly price in USD.
    /// </summary>
    public decimal MonthlyPriceUsd { get; private set; }

    /// <summary>
    /// Gets the annual price in USD.
    /// </summary>
    public decimal? AnnualPriceUsd { get; private set; }

    /// <summary>
    /// Gets the maximum number of products per batch.
    /// </summary>
    public int MaxBatchSize { get; private set; } = 100;

    /// <summary>
    /// Gets the maximum number of products per month.
    /// </summary>
    public int MaxProductsPerMonth { get; private set; } = 1_000;

    /// <summary>
    /// Gets the maximum number of concurrent jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; private set; } = 1;

    /// <summary>
    /// Gets the image-generation quota.
    /// </summary>
    public int ImageGenerationQuota { get; private set; } = 500;

    /// <summary>
    /// Gets the video-generation quota.
    /// </summary>
    public int VideoGenerationQuota { get; private set; } = 50;

    /// <summary>
    /// Gets the API call quota.
    /// </summary>
    public int ApiCallQuota { get; private set; } = 10_000;

    /// <summary>
    /// Gets the storage quota in gigabytes.
    /// </summary>
    public decimal StorageQuotaGb { get; private set; } = 100;

    /// <summary>
    /// Gets plan feature metadata as JSON.
    /// </summary>
    public string Features { get; private set; } = "{}";

    /// <summary>
    /// Gets a value indicating whether priority support is included.
    /// </summary>
    public bool PrioritySupport { get; private set; }

    /// <summary>
    /// Gets a value indicating whether custom API keys are allowed.
    /// </summary>
    public bool CustomApiKeysAllowed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the plan is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the plan display order.
    /// </summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Gets subscriptions associated with this plan.
    /// </summary>
    public IReadOnlyCollection<Subscription> Subscriptions => _subscriptions.AsReadOnly();
}
