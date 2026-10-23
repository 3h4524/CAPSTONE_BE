using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class RagChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string Content { get; set; } = null!;

    public int ChunkIndex { get; set; }

    public int TokenCount { get; set; }

    public string Metadata { get; set; } = null!;

    public virtual RagDocument Document { get; set; } = null!;

    public virtual ICollection<RagEmbedding> RagEmbeddings { get; set; } = new List<RagEmbedding>();
}
