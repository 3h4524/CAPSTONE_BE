using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a purchasable subscription plan.
/// </summary>
public sealed class SubscriptionPlan : AuditableEntity
{
    private readonly List<PlanFeature> _features = [];
    private readonly List<Subscription> _subscriptions = [];

    private SubscriptionPlan()
    {
    }

    /// <summary>
    /// Gets the plan name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the plan tier.
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
    /// Gets the maximum number of concurrent batch jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; private set; } = 1;

    /// <summary>
    /// Gets the monthly image-generation quota.
    /// </summary>
    public int ImageGenerationQuota { get; private set; } = 500;

    /// <summary>
    /// Gets the monthly video-generation quota.
    /// </summary>
    public int VideoGenerationQuota { get; private set; } = 50;

    /// <summary>
    /// Gets the monthly API-call quota.
    /// </summary>
    public int ApiCallQuota { get; private set; } = 10_000;

    /// <summary>
    /// Gets the storage quota in gigabytes.
    /// </summary>
    public decimal StorageQuotaGb { get; private set; } = 100m;

    /// <summary>
    /// Gets a value indicating whether the plan includes priority support.
    /// </summary>
    public bool PrioritySupport { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the plan allows custom API keys.
    /// </summary>
    public bool CustomApiKeysAllowed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the plan is offered.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the display sort order.
    /// </summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// Gets the feature flags attached to the plan.
    /// </summary>
    public IReadOnlyCollection<PlanFeature> Features => _features.AsReadOnly();

    /// <summary>
    /// Gets the subscriptions sold on this plan.
    /// </summary>
    public IReadOnlyCollection<Subscription> Subscriptions => _subscriptions.AsReadOnly();
}
