namespace APCS.Application.Features.Rag.Dtos;

public sealed record RagSearchResultDto(
    Guid ChunkId,
    Guid DocumentId,
    string Title,
    string ContentType,
    string Content,
    string? SourceUrl,
    double Similarity,
    IReadOnlyDictionary<string, object?> Metadata,
    string? Version = null,
    string Status = "ready");
