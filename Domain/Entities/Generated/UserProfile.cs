using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class UserProfile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? ShopName { get; set; }

    public string? ShopDescription { get; set; }

    public string Timezone { get; set; } = null!;

    public string Language { get; set; } = null!;

    public string? ThemePreference { get; set; }

    public bool? NotificationEmailEnabled { get; set; }

    public bool? NewsletterSubscribed { get; set; }

    public bool? TwoFactorEnabled { get; set; }

    public decimal? ProfileCompletionPercentage { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
