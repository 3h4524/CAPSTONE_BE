using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MarketOpportunityEvidence
{
    public Guid OpportunityId { get; set; }

    public Guid SnapshotId { get; set; }

    public Guid? RagDocumentId { get; set; }

    public string EvidenceRole { get; set; } = null!;

    public int EvidenceRank { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual MarketOpportunity Opportunity { get; set; } = null!;

    public virtual RagDocument? RagDocument { get; set; }

    public virtual MarketListingSnapshot Snapshot { get; set; } = null!;
}
