using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class UsageStatistic
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public DateOnly BillingPeriodStart { get; set; }

    public DateOnly BillingPeriodEnd { get; set; }

    public int? ImagesGenerated { get; set; }

    public int? VideosCreated { get; set; }

    public int? ListingsExported { get; set; }

    public int? TotalApiCalls { get; set; }

    public decimal? TotalApiCostUsd { get; set; }

    public decimal? StorageUsedGb { get; set; }

    public int? BatchJobsCompleted { get; set; }

    public decimal? AverageProcessingTimeSeconds { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
