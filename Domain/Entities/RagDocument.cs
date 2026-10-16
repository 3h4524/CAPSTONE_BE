namespace APCS.Domain.Entities;

public sealed class RagDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = null!;
    public string SourceType { get; set; } = null!;

    public string ContentType { get; set; } = "listing";
    public string? SourceUrl { get; set; }
    public string? Version { get; set; }
    public string Language { get; set; } = "en";
    public string ContentHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<RagChunk> Chunks { get; set; } = new List<RagChunk>();
}
