using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Requests a short-lived, exact-cost consent before paid Veo generation.</summary>
public sealed record AiShotQuoteRequest(GenerateVideoConfig? Config = null, bool Regenerate = false);
/// <summary>A quote bound to one run, storyboard, and set of missing AI clips.</summary>
public sealed record AiShotQuoteResponse(string Token, decimal EstimatedCostUsd, int NewClips, DateTimeOffset ExpiresAtUtc,
    string Model, string Note);

public interface IAiShotQuoteService
{
    Task<Result<AiShotQuoteResponse>> QuoteAsync(Guid runId, AiShotQuoteRequest request, CancellationToken ct);
    Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, CancellationToken ct);
}

public sealed class AiShotQuoteService(ICurrentUser user, IRepository<WorkflowRun> runs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MediaJob> jobs, IApiKeyRepository keys, WorkflowRuntime runtime, IAiShotQuoteSigner signer,
    TimeProvider time) : IAiShotQuoteService
{
    public async Task<Result<AiShotQuoteResponse>> QuoteAsync(Guid runId, AiShotQuoteRequest request, CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(runId, ct);
        if (run == null || run.UserId != user.UserId) return Result.Failure<AiShotQuoteResponse>(Error.NotFound("RunNotFound", "Run not found."));
        if (run.Status is not ("waiting_for_input" or "waiting_for_review" or "failed"))
            return Result.Failure<AiShotQuoteResponse>(Error.Conflict("InvalidCheckpoint", "The run is not ready for AI Shot confirmation."));
        var node = (await nodes.FindAsync(x => x.WorkflowRunId == runId && x.NodeType == "generate-video", ct)).Single();
        var config = request.Config ?? WorkflowJson.Read<GenerateVideoConfig>(node.InputSnapshot);
        var evaluated = await EvaluateAsync(run, config, request.Regenerate, ct);
        if (evaluated.IsFailure) return Result.Failure<AiShotQuoteResponse>(evaluated.Error);
        var (board, cost, count) = evaluated.Value;
        var expires = time.GetUtcNow().AddMinutes(10);
        return Result.Success(new AiShotQuoteResponse(signer.Sign(new(run.Id, board.Fingerprint, request.Regenerate, cost, expires)),
            cost, count, expires, "veo-3.1-lite-generate-preview", "Estimate only. Google bills the connected account for successful generations."));
    }

    public async Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Success();
        var claims = string.IsNullOrWhiteSpace(token) ? null : signer.Read(token);
        if (claims == null || claims.ExpiresAtUtc <= time.GetUtcNow() || claims.RunId != run.Id || claims.Regenerate != regenerate)
            return Result.Failure(Error.Validation("Confirm the current AI Shot cost before continuing."));
        var evaluated = await EvaluateAsync(run, config, regenerate, ct);
        if (evaluated.IsFailure) return Result.Failure(evaluated.Error);
        var (board, cost, _) = evaluated.Value;
        return claims.StoryboardFingerprint == board.Fingerprint && claims.EstimatedCostUsd == cost
            ? Result.Success() : Result.Failure(Error.Conflict("AiShotQuoteChanged", "AI Shot settings or source changed. Review the cost again."));
    }

    private async Task<Result<(Storyboard Board, decimal Cost, int Count)>> EvaluateAsync(WorkflowRun run, GenerateVideoConfig config, bool regenerate, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("Choose AI Shot to request a quote."));
        var key = (await keys.ListOwnedAsync(run.UserId, ct)).FirstOrDefault(x => x.ServiceProvider == "gemini" && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key == null) return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("Connect a Gemini API key with paid Veo access first."));
        var planned = await runtime.PlanAsync(run, config, ct);
        if (planned.IsFailure) return Result.Failure<(Storyboard, decimal, int)>(planned.Error);
        var board = planned.Value;
        var node = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).Single();
        var previous = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id, ct)).SingleOrDefault();
        AiShotJobState[] stored = [];
        if (!regenerate && previous?.Kind == "render")
        {
            try { stored = WorkflowJson.Read<AiRenderJobPayload>(previous.Payload).AiShotStates?.ToArray() ?? []; }
            catch (System.Text.Json.JsonException) { /* An older Standard job has no AI states. */ }
        }
        var missing = (board.AiShots ?? []).Where(shot => !stored.Any(state => state.Fingerprint == shot.Fingerprint &&
            state.Status == "stored" && !string.IsNullOrEmpty(state.StorageKey) && !string.IsNullOrEmpty(state.StorageVersion))).ToArray();
        return Result.Success((board, missing.Sum(x => x.DurationSeconds) * .05m, missing.Length));
    }
}
