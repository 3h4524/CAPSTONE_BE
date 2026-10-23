using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class OpportunityAnalysisRun
{
    public Guid Id { get; set; }

    public Guid SellerOpportunityId { get; set; }

    public string Status { get; set; } = null!;

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string PromptVersion { get; set; } = null!;

    public string SchemaVersion { get; set; } = null!;

    public string InputSnapshot { get; set; } = null!;

    public string? OutputSnapshot { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual SellerOpportunity SellerOpportunity { get; set; } = null!;
}
