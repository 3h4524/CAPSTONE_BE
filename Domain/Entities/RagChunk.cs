namespace APCS.Domain.Entities;

public sealed class RagChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public int TokenCount { get; set; }
    public string Metadata { get; set; } = "{}";
    public RagDocument Document { get; set; } = null!;
    public ICollection<RagEmbedding> Embeddings { get; set; } = new List<RagEmbedding>();
}
