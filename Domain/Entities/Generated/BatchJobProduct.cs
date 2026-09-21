using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class BatchJobProduct
{
    public Guid Id { get; set; }

    public Guid BatchJobId { get; set; }

    public Guid? ProductId { get; set; }

    public int SequenceOrder { get; set; }

    public int? SourceRowIndex { get; set; }

    public string? RawRowData { get; set; }

    public string Status { get; set; } = null!;

    public string? CurrentStep { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public decimal? DurationSeconds { get; set; }

    public string? ErrorMessage { get; set; }

    public int? RetryCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid BatchId { get; set; }

    public virtual ICollection<AiPrompt> AiPrompts { get; set; } = new List<AiPrompt>();
    public string? CustomSubject { get; set; }

    public string? CustomArtStyle { get; set; }

    public string? CustomMoodTone { get; set; }

    public string? CustomNegativeTerms { get; set; }

    public string? CustomInstructions { get; set; }

    public virtual BatchJob BatchJob { get; set; } = null!;

    public virtual ICollection<BatchJobLog> BatchJobLogs { get; set; } = new List<BatchJobLog>();

    public virtual ICollection<DesignImage> DesignImages { get; set; } = new List<DesignImage>();

    public virtual ICollection<ListingContent> ListingContents { get; set; } = new List<ListingContent>();

    public virtual ICollection<MockupImage> MockupImages { get; set; } = new List<MockupImage>();

    public virtual Product? Product { get; set; }

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();
}
