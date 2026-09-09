using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class DesignImage
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid AiPromptId { get; set; }

    public Guid? BatchJobId { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public string ImageGeneratorModel { get; set; } = null!;

    public string StorageProvider { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;

    public int ImageWidthPx { get; set; }

    public int ImageHeightPx { get; set; }

    public string FileFormat { get; set; } = null!;

    public decimal FileSizeMb { get; set; }

    public decimal? QualityScore { get; set; }

    public int? UserRating { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public bool? IsFinal { get; set; }

    public int VariationIndex { get; set; }

    public decimal GenerationTimeSeconds { get; set; }

    public string? GenerationMetadata { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual AiPrompt AiPrompt { get; set; } = null!;

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual ICollection<MockupImage> MockupImages { get; set; } = new List<MockupImage>();

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PromoVideoScene> PromoVideoScenes { get; set; } = new List<PromoVideoScene>();
}
