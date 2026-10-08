using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MockupImage
{
    public Guid Id { get; set; }

    public Guid? DesignImageId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? MockupTemplateId { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public string StorageProvider { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public string? MockupImageUrl { get; set; }

    public int MockupWidthPx { get; set; }

    public int MockupHeightPx { get; set; }

    public decimal? GenerationTimeSeconds { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public bool? IsFinal { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? BatchJobProductId { get; set; }

    public string? GarmentColor { get; set; }

    public string SourceType { get; set; } = null!;

    public string? ContentHash { get; set; }

    public long MetadataRevision { get; set; }

    public string? ArtworkGroupKey { get; set; }

    public string? VariantKey { get; set; }

    public string Role { get; set; } = null!;

    public string Regions { get; set; } = null!;

    public string? StorageVersion { get; set; }

    public long? ApprovedRevision { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual BatchJobProduct? BatchJobProduct { get; set; }

    public virtual DesignImage? DesignImage { get; set; }

    public virtual MockupTemplate? MockupTemplate { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PromoVideoScene> PromoVideoScenes { get; set; } = new List<PromoVideoScene>();
}
