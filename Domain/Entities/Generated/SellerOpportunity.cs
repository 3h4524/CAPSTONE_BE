using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SellerOpportunity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OpportunityId { get; set; }

    public decimal SellerFitScore { get; set; }

    public decimal FinalScore { get; set; }

    public int? Rank { get; set; }

    public string State { get; set; } = null!;

    public string? FitExplanation { get; set; }

    public string FitBreakdown { get; set; } = null!;

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastEvaluatedAt { get; set; }

    public DateTime? ActedAt { get; set; }

    public virtual MarketOpportunity Opportunity { get; set; } = null!;

    public virtual ICollection<OpportunityAnalysisRun> OpportunityAnalysisRuns { get; set; } = new List<OpportunityAnalysisRun>();

    public virtual User User { get; set; } = null!;
}
