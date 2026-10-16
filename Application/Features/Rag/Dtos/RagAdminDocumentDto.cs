namespace APCS.Application.Features.Rag.Dtos;

public sealed record RagAdminDocumentDto(
    Guid Id,
    string SourceId,
    string Title,
    string SourceType,
    string ContentType,
    string? SourceUrl,
    string? Version,
    string Language,
    string Status,
    string? IngestionError,
    int ChunkCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record RagAdminChunkDto(
    Guid Id,
    int ChunkIndex,
    int TokenCount,
    string Content,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record RagAdminDocumentDetailDto(
    RagAdminDocumentDto Document,
    IReadOnlyList<RagAdminChunkDto> Chunks);
