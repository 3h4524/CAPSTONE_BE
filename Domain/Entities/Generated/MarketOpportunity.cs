using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MarketOpportunity
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Niche { get; set; } = null!;

    public string? Category { get; set; }

    public string? Summary { get; set; }

    public decimal MarketOpportunityScore { get; set; }

    public decimal Confidence { get; set; }

    public string ScoreBreakdown { get; set; } = null!;

    public string FormulaVersion { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime FirstObservedAt { get; set; }

    public DateTime LastObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MarketOpportunityEvidence> MarketOpportunityEvidences { get; set; } = new List<MarketOpportunityEvidence>();

    public virtual ICollection<SellerOpportunity> SellerOpportunities { get; set; } = new List<SellerOpportunity>();
}
