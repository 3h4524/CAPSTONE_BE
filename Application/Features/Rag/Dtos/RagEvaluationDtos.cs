namespace APCS.Application.Features.Rag.Dtos;

public sealed record RagEvaluationRequest(
    IReadOnlyList<RagEvaluationCaseRequest> Cases,
    int TopK = 5);

public sealed record RagEvaluationCaseRequest(
    string Id,
    RagSearchRequest Search,
    string? ExpectedSourceId = null,
    Guid? ExpectedDocumentId = null,
    string? ExpectedText = null,
    string Category = "retrieval",
    string? ContentType = null);

public sealed record RagEvaluationTypeMetricsDto(
    string ContentType,
    int CaseCount,
    double RetrievalScore,
    double CitationScore,
    double ContextCoverageScore,
    double AbstentionScore,
    double AverageLatencyMs);

public sealed record RagEvaluationResponse(
    string RunId,
    DateTimeOffset CompletedAt,
    int CaseCount,
    double RetrievalScore,
    double CitationScore,
    double ContextCoverageScore,
    double AbstentionScore,
    double AverageLatencyMs,
    IReadOnlyList<RagEvaluationCaseResultDto> Cases,
    IReadOnlyList<RagEvaluationTypeMetricsDto>? TypeMetrics = null);

public sealed record RagEvaluationCaseResultDto(
    string Id,
    string Category,
    string Query,
    string Verdict,
    bool ExpectedMatch,
    double RetrievalScore,
    double CitationScore,
    double ContextCoverageScore,
    double LatencyMs,
    string? Failure,
    IReadOnlyList<RagSearchDiagnosticResultDto> Results,
    string ContentType = "mixed",
    RagSearchDiagnosticsDto? Diagnostics = null);
