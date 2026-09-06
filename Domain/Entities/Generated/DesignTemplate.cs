using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class DesignTemplate
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string? NicheCategory { get; set; }

    public string? ArtStyle { get; set; }

    public string BasePrompt { get; set; } = null!;

    public string ExamplePrompts { get; set; } = null!;

    public string? StyleDescription { get; set; }

    public string? PreviewImageUrl { get; set; }

    public bool? IsSystemTemplate { get; set; }

    public bool? IsActive { get; set; }

    public int? UsageCount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<AiPrompt> AiPrompts { get; set; } = new List<AiPrompt>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual User? User { get; set; }
}
