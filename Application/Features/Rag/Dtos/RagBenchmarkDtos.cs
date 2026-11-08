namespace APCS.Application.Features.Rag.Dtos;

/// <summary>A ready listing document a known-item case can be built from.</summary>
public sealed record RagKnownItemSource(string SourceId, string? Niche, string Content);

/// <summary>
/// Runs the known-item benchmark. Without <see cref="CaseSetId"/> a new case set is sampled from the
/// ready listing documents (deterministically for a given <see cref="Seed"/>) and saved; with it, the
/// saved set is run again so two runs differ only by the retrieval code or settings.
/// </summary>
public sealed record RagKnownItemBenchmarkRequest(
    int SampleSize = 20,
    int? Seed = null,
    string? CaseSetId = null);

/// <summary>A saved set of known-item cases: each case's expected answer is the listing its query came from.</summary>
public sealed record RagKnownItemCaseSet(
    string Id,
    int Seed,
    int SampleSize,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RagEvaluationCaseRequest> Cases);

public sealed record RagKnownItemBenchmarkResponse(
    string CaseSetId,
    int Seed,
    int SampleSize,
    DateTimeOffset CaseSetCreatedAt,
    bool ReusedCaseSet,
    int TopK,
    RagEvaluationResponse Evaluation);
