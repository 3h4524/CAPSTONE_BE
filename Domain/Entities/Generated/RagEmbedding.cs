using System;
using System.Collections.Generic;
using Pgvector;

namespace APCS.Domain.Entities;

public partial class RagEmbedding
{
    public Guid Id { get; set; }

    public Guid ChunkId { get; set; }

    public Vector Embedding { get; set; } = null!;

    public string EmbeddingModel { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual RagChunk Chunk { get; set; } = null!;
}
