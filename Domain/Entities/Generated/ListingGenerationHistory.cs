using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ListingGenerationHistory
{
    public Guid Id { get; set; }

    public Guid ListingContentId { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public int GenerationNumber { get; set; }

    public string PromptUsed { get; set; } = null!;

    public string? AdjustmentHint { get; set; }

    public string RawAiResponse { get; set; } = null!;

    public string? TitleGenerated { get; set; }

    public List<string>? TagsGenerated { get; set; }

    public string? DescriptionGenerated { get; set; }

    public string UserAction { get; set; } = null!;

    public string? FeedbackNotes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual ListingContent ListingContent { get; set; } = null!;
}
