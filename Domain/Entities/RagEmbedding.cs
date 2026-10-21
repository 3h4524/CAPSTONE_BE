using Pgvector;

namespace APCS.Domain.Entities;

public sealed class RagEmbedding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChunkId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string EmbeddingModel { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public RagChunk Chunk { get; set; } = null!;
}
