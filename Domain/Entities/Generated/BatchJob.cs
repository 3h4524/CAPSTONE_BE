using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class BatchJob
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string SourceFileType { get; set; } = null!;

    public string? SourceFileUrl { get; set; }

    public string? SourceFileHash { get; set; }

    public string Status { get; set; } = null!;

    public decimal? ProgressPercentage { get; set; }

    public int? TotalProducts { get; set; }

    public int? ProcessedProducts { get; set; }

    public int? FailedProducts { get; set; }

    public int? SkippedProducts { get; set; }

    public string Config { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public decimal? EstimatedCostUsd { get; set; }

    public decimal? ActualCostUsd { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? EstimatedCompletionTime { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<ApiUsageRecord> ApiUsageRecords { get; set; } = new List<ApiUsageRecord>();

    public virtual ICollection<BatchJobLog> BatchJobLogs { get; set; } = new List<BatchJobLog>();

    public virtual ICollection<BatchJobProduct> BatchJobProducts { get; set; } = new List<BatchJobProduct>();

    public virtual ICollection<DesignImage> DesignImages { get; set; } = new List<DesignImage>();

    public virtual ICollection<EtsyUploadLog> EtsyUploadLogs { get; set; } = new List<EtsyUploadLog>();

    public virtual ICollection<ExportPackage> ExportPackages { get; set; } = new List<ExportPackage>();

    public virtual ICollection<ListingContent> ListingContents { get; set; } = new List<ListingContent>();

    public virtual ICollection<NotificationAlert> NotificationAlerts { get; set; } = new List<NotificationAlert>();

    public virtual ICollection<PrintifyUploadLog> PrintifyUploadLogs { get; set; } = new List<PrintifyUploadLog>();

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();

    public virtual User User { get; set; } = null!;
}
