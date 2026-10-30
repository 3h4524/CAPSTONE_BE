namespace APCS.Application.Features.Rag.Dtos;

public sealed record RagCorpusHealthResponse(
    DateTimeOffset GeneratedAt,
    string? ContentType,
    int StaleDays,
    int TotalDocuments,
    int ReadyDocuments,
    int QueuedDocuments,
    int ProcessingDocuments,
    int FailedDocuments,
    int SupersededDocuments,
    int TotalChunks,
    int TotalEmbeddings,
    int CurrentModelEmbeddings,
    int MissingEmbeddingChunkCount,
    int MissingMetadataDocumentCount,
    int MissingNicheDocumentCount,
    int MissingPlatformDocumentCount,
    int DuplicateContentHashGroupCount,
    int StaleReadyDocumentCount,
    IReadOnlyList<string> EmbeddingModels,
    IReadOnlyList<RagCorpusHealthTypeMetricsDto> ByContentType,
    IReadOnlyList<RagCorpusHealthIssueDto> Issues);

public sealed record RagCorpusHealthTypeMetricsDto(
    string ContentType,
    int DocumentCount,
    int ReadyDocuments,
    int QueuedDocuments,
    int ProcessingDocuments,
    int FailedDocuments,
    int SupersededDocuments,
    int ChunkCount,
    int EmbeddingCount,
    int CurrentModelEmbeddings,
    int MissingEmbeddingChunkCount,
    int MissingMetadataDocumentCount,
    int MissingNicheDocumentCount,
    int MissingPlatformDocumentCount,
    int StaleReadyDocumentCount);

public sealed record RagCorpusHealthIssueDto(
    string Code,
    string Severity,
    int Count,
    string Description);

public sealed record RagReingestRequest(
    IReadOnlyList<Guid>? DocumentIds = null,
    string? ContentType = null,
    bool FailedOnly = false,
    bool StaleOnly = false,
    bool MissingEmbeddingOnly = false,
    int StaleDays = 30,
    int MaxDocuments = 100);

public sealed record RagReingestResponse(
    int SelectedCount,
    int QueuedCount,
    int SkippedCount,
    IReadOnlyList<Guid> DocumentIds);
