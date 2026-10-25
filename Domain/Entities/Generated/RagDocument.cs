using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class RagDocument
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string SourceType { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public string? SourceUrl { get; set; }

    public string? Version { get; set; }

    public string Language { get; set; } = null!;

    public string ContentHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public string SourceId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? IngestionError { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string IngestionContent { get; set; } = null!;

    public string IngestionMetadata { get; set; } = null!;

    public virtual ICollection<MarketListingSnapshot> MarketListingSnapshots { get; set; } = new List<MarketListingSnapshot>();

    public virtual ICollection<MarketOpportunityEvidence> MarketOpportunityEvidences { get; set; } = new List<MarketOpportunityEvidence>();

    public virtual ICollection<RagChunk> RagChunks { get; set; } = new List<RagChunk>();
}
