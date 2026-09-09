using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Subscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public string BillingCycle { get; set; } = null!;

    public decimal MonthlyPriceUsd { get; set; }

    public decimal? AnnualPriceUsd { get; set; }

    public string Status { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly RenewalDate { get; set; }

    public DateTime? TrialEndsAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public bool? AutoRenew { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual SubscriptionPlan Plan { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
