namespace APCS.Application.Features.Rag.Dtos;

using APCS.Application.Features.Rag.Common;

public sealed record RagIngestRequest(
    string Title,
    string SourceType,
    string? SourceUrl,
    string? Version,
    string Language,
    string Content,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    string ContentType = RagContentTypes.Listing,
    string? SourceId = null);
