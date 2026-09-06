using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Product
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? DesignTemplateId { get; set; }

    public string Name { get; set; } = null!;

    public string ProductType { get; set; } = null!;

    public string? NicheCategory { get; set; }

    public string? TargetAudience { get; set; }

    public string InputDescription { get; set; } = null!;

    public string? DesiredDesignText { get; set; }

    public string? StylePreset { get; set; }

    public string? MainKeywords { get; set; }

    public string? ColorPreference { get; set; }

    public string? Notes { get; set; }

    public string ProcessingStatus { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AiPrompt> AiPrompts { get; set; } = new List<AiPrompt>();

    public virtual ICollection<ApiUsageRecord> ApiUsageRecords { get; set; } = new List<ApiUsageRecord>();

    public virtual ICollection<BatchJobProduct> BatchJobProducts { get; set; } = new List<BatchJobProduct>();

    public virtual ICollection<DesignImage> DesignImages { get; set; } = new List<DesignImage>();

    public virtual DesignTemplate? DesignTemplate { get; set; }

    public virtual ICollection<EtsyUploadLog> EtsyUploadLogs { get; set; } = new List<EtsyUploadLog>();

    public virtual ICollection<ExportPackageItem> ExportPackageItems { get; set; } = new List<ExportPackageItem>();

    public virtual ListingContent? ListingContent { get; set; }

    public virtual ICollection<MockupImage> MockupImages { get; set; } = new List<MockupImage>();

    public virtual ICollection<PrintifyUploadLog> PrintifyUploadLogs { get; set; } = new List<PrintifyUploadLog>();

    public virtual ICollection<ProductMockupTemplate> ProductMockupTemplates { get; set; } = new List<ProductMockupTemplate>();

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();

    public virtual ICollection<SocialMediaShare> SocialMediaShares { get; set; } = new List<SocialMediaShare>();

    public virtual User User { get; set; } = null!;
}
