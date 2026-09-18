using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class StyleArtPreset
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string StyleModifiers { get; set; } = null!;

    public string? PreviewImageUrl { get; set; }

    public string Recommendations { get; set; } = null!;

    public bool IsSystemTemplate { get; set; }

    public bool IsActive { get; set; }

    public int UsageCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
