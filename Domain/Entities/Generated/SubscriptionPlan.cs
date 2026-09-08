using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SubscriptionPlan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Tier { get; set; } = null!;

    public string? Description { get; set; }

    public decimal MonthlyPriceUsd { get; set; }

    public decimal? AnnualPriceUsd { get; set; }

    public int MaxBatchSize { get; set; }

    public int MaxProductsPerMonth { get; set; }

    public int MaxConcurrentJobs { get; set; }

    public int ImageGenerationQuota { get; set; }

    public int VideoGenerationQuota { get; set; }

    public int ApiCallQuota { get; set; }

    public decimal StorageQuotaGb { get; set; }

    public bool? PrioritySupport { get; set; }

    public bool? CustomApiKeysAllowed { get; set; }

    public bool? IsActive { get; set; }

    public int? SortOrder { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
