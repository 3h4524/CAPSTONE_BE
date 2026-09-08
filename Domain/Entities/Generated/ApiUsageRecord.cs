using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ApiUsageRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? BatchJobId { get; set; }

    public Guid? ProductId { get; set; }

    public string Provider { get; set; } = null!;

    public string Feature { get; set; } = null!;

    public string? ModelName { get; set; }

    public string? ProviderRequestId { get; set; }

    public int RequestUnits { get; set; }

    public int? TokensInput { get; set; }

    public int? TokensOutput { get; set; }

    public decimal CostUsd { get; set; }

    public int? LatencyMs { get; set; }

    public string Status { get; set; } = null!;

    public string? ErrorCode { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual ICollection<BatchJobLog> BatchJobLogs { get; set; } = new List<BatchJobLog>();

    public virtual ICollection<DesignImage> DesignImages { get; set; } = new List<DesignImage>();

    public virtual ICollection<ListingContent> ListingContents { get; set; } = new List<ListingContent>();

    public virtual ICollection<ListingGenerationHistory> ListingGenerationHistories { get; set; } = new List<ListingGenerationHistory>();

    public virtual ICollection<MockupImage> MockupImages { get; set; } = new List<MockupImage>();

    public virtual Product? Product { get; set; }

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();

    public virtual User User { get; set; } = null!;
}
