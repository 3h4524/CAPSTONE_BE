using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MusicTrack
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string ArtistName { get; set; } = null!;

    public int DurationSeconds { get; set; }

    public string Genre { get; set; } = null!;

    public string Mood { get; set; } = null!;

    public bool RoyaltyFree { get; set; }

    public string LicenseType { get; set; } = null!;

    public string? LicenseSource { get; set; }

    public string AudioUrl { get; set; } = null!;

    public string? PreviewUrl { get; set; }

    public bool? IsAvailable { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();
}
