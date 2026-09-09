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

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual MusicTrack? MusicTrack { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PromoVideoScene> PromoVideoScenes { get; set; } = new List<PromoVideoScene>();

    public virtual ICollection<SocialMediaShare> SocialMediaShares { get; set; } = new List<SocialMediaShare>();

    public virtual VideoTemplate VideoTemplate { get; set; } = null!;
}
