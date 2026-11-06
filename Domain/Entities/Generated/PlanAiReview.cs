using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PlanAiReview
{
    public Guid Id { get; set; }

    public string Status { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string? Model { get; set; }

    public string InputJson { get; set; } = null!;

    public string? ReviewJson { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public string? ErrorMessage { get; set; }

    public Guid? TriggeredBy { get; set; }

    public DateTime GeneratedAt { get; set; }

    public virtual User? TriggeredByNavigation { get; set; }
}
