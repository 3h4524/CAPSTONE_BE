using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SellerResearchProfile
{
    public Guid UserId { get; set; }

    public string? ShopGoal { get; set; }

    public string TargetAudiences { get; set; } = null!;

    public string PreferredCategories { get; set; } = null!;

    public string ExcludedCategories { get; set; } = null!;

    public string PreferredKeywords { get; set; } = null!;

    public decimal? MinimumPrice { get; set; }

    public decimal? MaximumPrice { get; set; }

    public int ProfileVersion { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
