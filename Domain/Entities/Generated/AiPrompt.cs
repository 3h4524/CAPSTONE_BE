using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AiPrompt
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid? DesignTemplateId { get; set; }

    public string OriginalDescription { get; set; } = null!;

    public string SystemPrompt { get; set; } = null!;

    public string? FewShotExamples { get; set; }

    public string GeneratedPrompt { get; set; } = null!;

    public int VersionNumber { get; set; }

    public bool? IsApprovedByUser { get; set; }

    public string? UserNotes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public virtual ICollection<DesignImage> DesignImages { get; set; } = new List<DesignImage>();

    public virtual DesignTemplate? DesignTemplate { get; set; }

    public virtual Product Product { get; set; } = null!;
}
