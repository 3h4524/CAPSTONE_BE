namespace APCS.Application.Features.Rag.Dtos;

public sealed record RagSearchDiagnosticsDto(
    string Query,
    int RequestedTopK,
    int CandidateLimit,
    int VectorCandidateCount,
    int LexicalCandidateCount,
    int MetadataMatchedCandidateCount,
    int RerankedCandidateCount,
    double DurationMs,
    IReadOnlyList<RagSearchDiagnosticResultDto> Results,
    int CandidatesBeforeMetadataFilter = 0,
    int MetadataFilteredCandidateCount = 0,
    int ThresholdRejectedCandidateCount = 0,
    double BestSemanticScore = 0,
    double BestLexicalScore = 0,
    double BestFinalScore = 0,
    string Status = "no_results",
    string Confidence = "none",
    IReadOnlyList<string>? AppliedFilters = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record RagSearchDiagnosticResultDto(
    RagSearchResultDto Result,
    double SemanticScore,
    double LexicalScore,
    double ViralScore,
    double FreshnessScore,
    double FinalScore);
