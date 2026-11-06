using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SubscriptionFeatureGrant
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public Guid PlanId { get; set; }

    public string FeatureCode { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public int? LimitValue { get; set; }

    public DateOnly ExpiresOn { get; set; }

    public string Reason { get; set; } = null!;

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Feature FeatureCodeNavigation { get; set; } = null!;

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual Subscription Subscription { get; set; } = null!;
}
