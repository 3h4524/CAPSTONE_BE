using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingContent
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid? BatchJobId { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public string AiModelUsed { get; set; } = null!;

    public string ModelVersion { get; set; } = null!;

    public decimal GenerationTimeSeconds { get; set; }

    public string ApprovalStatus { get; set; } = null!;

    public DateTime? ApprovedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual ICollection<ListingDescription> ListingDescriptions { get; set; } = new List<ListingDescription>();

    public virtual ICollection<ListingGenerationHistory> ListingGenerationHistories { get; set; } = new List<ListingGenerationHistory>();

    public virtual ICollection<ListingTag> ListingTags { get; set; } = new List<ListingTag>();

    public virtual ICollection<ListingTitle> ListingTitles { get; set; } = new List<ListingTitle>();

    public virtual Product Product { get; set; } = null!;

    public virtual SeoScore? SeoScore { get; set; }
}
