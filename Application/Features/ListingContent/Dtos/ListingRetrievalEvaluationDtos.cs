using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Features.ListingContent.Dtos;

public sealed record ListingRetrievalEvaluationRequest(Guid ProductId);

public sealed record ListingRetrievalEvaluationResponse(
    Guid ProductId,
    string ProductName,
    string ProductType,
    string? Niche,
    string? TargetAudience,
    string Description,
    IReadOnlyList<string> Keywords,
    string? Notes,
    string ProductionQuery,
    double DurationMs,
    RagSearchDiagnosticsDto PolicyContext,
    RagSearchDiagnosticsDto ListingContext);
