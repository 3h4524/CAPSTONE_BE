using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PromoVideo
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid? BatchJobId { get; set; }

    public Guid VideoTemplateId { get; set; }

    public Guid? MusicTrackId { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public string? TextOverlayContent { get; set; }

    public string? TextOverlayColor { get; set; }

    public string? TextOverlayFont { get; set; }

    public string? StorageProvider { get; set; }

    public string? StorageKey { get; set; }

    public string? VideoUrl { get; set; }

    public int VideoDurationSeconds { get; set; }

    public string VideoResolution { get; set; } = null!;

    public string FileFormat { get; set; } = null!;

    public string AspectRatio { get; set; } = null!;

    public decimal? FileSizeMb { get; set; }

    public string PlatformTarget { get; set; } = null!;

    public decimal? QualityScore { get; set; }

    public int? UserRating { get; set; }

    public string Status { get; set; } = null!;

    public string ApprovalStatus { get; set; } = null!;

    public bool? IsFinal { get; set; }

    public decimal? GenerationTimeSeconds { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? BatchJobProductId { get; set; }

    public string Mode { get; set; } = null!;

    public Guid? SeriesId { get; set; }

    public int VersionNumber { get; set; }

    public Guid? WorkflowRunId { get; set; }

    public string? Fingerprint { get; set; }

    public int TemplateVersion { get; set; }

    public string ConfigSnapshot { get; set; } = null!;

    public string QaResult { get; set; } = null!;

    public long ReviewRevision { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? ThumbnailStorageKey { get; set; }

    public string? StorageVersion { get; set; }

    public string? ThumbnailStorageVersion { get; set; }

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual BatchJobProduct? BatchJobProduct { get; set; }

    public virtual ICollection<ExportPackageItem> ExportPackageItems { get; set; } = new List<ExportPackageItem>();

    public virtual MusicTrack? MusicTrack { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PromoVideoScene> PromoVideoScenes { get; set; } = new List<PromoVideoScene>();

    public virtual ICollection<SocialMediaShare> SocialMediaShares { get; set; } = new List<SocialMediaShare>();

    public virtual VideoTemplate VideoTemplate { get; set; } = null!;

    public virtual WorkflowRun? WorkflowRun { get; set; }
}
