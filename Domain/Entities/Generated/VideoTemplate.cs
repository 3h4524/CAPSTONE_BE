using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class VideoTemplate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public int DurationSeconds { get; set; }

    public string AspectRatio { get; set; } = null!;

    public string Resolution { get; set; } = null!;

    public string EffectsConfig { get; set; } = null!;

    public string? PreviewVideoUrl { get; set; }

    public bool? IsSystemTemplate { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();
}
