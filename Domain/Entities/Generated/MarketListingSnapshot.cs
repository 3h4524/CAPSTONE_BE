using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MarketListingSnapshot
{
    public Guid Id { get; set; }

    public string Platform { get; set; } = null!;

    public string ExternalListingId { get; set; } = null!;

    public string? ExternalSellerId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? SourceUrl { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public int? Quantity { get; set; }

    public int? Favorites { get; set; }

    public int? Views { get; set; }

    public string Tags { get; set; } = null!;

    public string? MatchedQuery { get; set; }

    public DateTime ObservedAt { get; set; }

    public DateTime CollectedAt { get; set; }

    public string? RawPayload { get; set; }

    public Guid? RagDocumentId { get; set; }

    public virtual ICollection<MarketOpportunityEvidence> MarketOpportunityEvidences { get; set; } = new List<MarketOpportunityEvidence>();

    public virtual RagDocument? RagDocument { get; set; }
}
