using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PlanFeature
{
    public Guid PlanId { get; set; }

    public string FeatureCode { get; set; } = null!;

    public bool? IsEnabled { get; set; }

    public int? LimitValue { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual SubscriptionPlan Plan { get; set; } = null!;
}
