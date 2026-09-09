using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SocialMediaShare
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid PromoVideoId { get; set; }

    public string Platform { get; set; } = null!;

    public string ShareStatus { get; set; } = null!;

    public DateTime? ScheduledTime { get; set; }

    public DateTime? PostedTime { get; set; }

    public string? PostCaption { get; set; }

    public string? ExternalPostId { get; set; }

    public string? ExternalPlatformUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public int? RetryCount { get; set; }

    public string? ApiResponse { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual PromoVideo PromoVideo { get; set; } = null!;

    public virtual ICollection<ShareHashtag> ShareHashtags { get; set; } = new List<ShareHashtag>();
}
