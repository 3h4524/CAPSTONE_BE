namespace APCS.Application.Features.Rag.Dtos;

using APCS.Application.Features.Rag.Common;

public sealed record RagSearchRequest(
    string Query,
    int TopK = 5,
    string? SourceType = null,
    string? Language = null,
    string? ContentType = null);
