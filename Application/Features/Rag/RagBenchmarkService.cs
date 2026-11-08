using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Features.Rag;

public interface IRagBenchmarkService
{
    /// <summary>Returns null when <see cref="RagKnownItemBenchmarkRequest.CaseSetId"/> names no saved case set.</summary>
    Task<RagKnownItemBenchmarkResponse?> RunKnownItemAsync(
        RagKnownItemBenchmarkRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Known-item retrieval benchmark. The corpus grows every day, so a case set is saved and can be run
/// again: comparing two runs of the same set shows the effect of a retrieval change and nothing else.
/// </summary>
public sealed class RagBenchmarkService(
    IRagService ragService,
    ICacheService cache,
    TimeProvider timeProvider) : IRagBenchmarkService
{
    public const int MinimumSampleSize = 5;
    // Two cases per listing; RagService.EvaluateAsync accepts at most 100 cases per run.
    public const int MaximumSampleSize = 40;

    private static readonly TimeSpan CaseSetTtl = TimeSpan.FromDays(180);

    public async Task<RagKnownItemBenchmarkResponse?> RunKnownItemAsync(
        RagKnownItemBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reused = !string.IsNullOrWhiteSpace(request.CaseSetId);
        var caseSet = reused
            ? await cache.GetAsync<RagKnownItemCaseSet>(CacheKey(request.CaseSetId!.Trim()), cancellationToken)
            : await CreateCaseSetAsync(request, cancellationToken);
        if (caseSet is null)
            return null;

        var evaluation = await ragService.EvaluateAsync(
            new RagEvaluationRequest(caseSet.Cases, RagKnownItemCaseBuilder.TopK),
            cancellationToken);
        return new RagKnownItemBenchmarkResponse(
            caseSet.Id,
            caseSet.Seed,
            caseSet.SampleSize,
            caseSet.CreatedAt,
            reused,
            RagKnownItemCaseBuilder.TopK,
            evaluation);
    }

    private async Task<RagKnownItemCaseSet> CreateCaseSetAsync(
        RagKnownItemBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        var sampleSize = Math.Clamp(request.SampleSize, MinimumSampleSize, MaximumSampleSize);
        var seed = request.Seed ?? Random.Shared.Next(1, int.MaxValue);
        var sources = await ragService.GetKnownItemSourcesAsync(cancellationToken);
        var cases = RagKnownItemCaseBuilder.Build(sources, sampleSize, seed);
        if (cases.Count == 0)
            throw new InvalidOperationException("No ready listing document has tags or a description to build a known-item case from.");

        var caseSet = new RagKnownItemCaseSet(
            Guid.NewGuid().ToString("N"),
            seed,
            cases.Count / 2,
            timeProvider.GetUtcNow(),
            cases);
        await cache.SetAsync(CacheKey(caseSet.Id), caseSet, CaseSetTtl, cancellationToken);
        return caseSet;
    }

    private static string CacheKey(string caseSetId) => $"rag:benchmark:known-item:{caseSetId}";
}
