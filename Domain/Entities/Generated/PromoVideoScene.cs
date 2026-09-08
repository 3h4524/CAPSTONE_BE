using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PromoVideoScene
{
    public Guid Id { get; set; }

    public Guid PromoVideoId { get; set; }

    public Guid? DesignImageId { get; set; }

    public Guid? MockupImageId { get; set; }

    public int SceneOrder { get; set; }

    public decimal DurationSeconds { get; set; }

    public string? TransitionEffect { get; set; }

    public string? TextOverlayContent { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual DesignImage? DesignImage { get; set; }

    public virtual MockupImage? MockupImage { get; set; }

    public virtual PromoVideo PromoVideo { get; set; } = null!;
}
