using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Requests a short-lived, exact-cost consent before paid AI video generation.</summary>
public sealed record AiShotQuoteRequest(GenerateVideoConfig? Config = null, bool Regenerate = false, int? SceneOrder = null,
    string? ExpectedStoryboardFingerprint = null);
/// <summary>A quote bound to one run, storyboard, and set of missing AI clips.</summary>
public sealed record AiShotQuoteResponse(string Token, decimal EstimatedCostUsd, int NewClips, DateTimeOffset ExpiresAtUtc,
    string Model, string Note);

public interface IAiShotQuoteService
{
    Task<Result<AiShotQuoteResponse>> QuoteAsync(Guid runId, AiShotQuoteRequest request, CancellationToken ct);
    Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, int? sceneOrder, CancellationToken ct);
}

public sealed class AiShotQuoteService(ICurrentUser user, IRepository<WorkflowRun> runs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MediaJob> jobs, IAiVideoCredentialResolver videoCredentials, WorkflowRuntime runtime, IAiShotQuoteSigner signer,
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
        if (!HasAiVideoScene(config))
            return Result.Failure<AiShotQuoteResponse>(Error.Validation("This storyboard has no AI video scenes to quote."));
        if (request.SceneOrder.HasValue && !request.Regenerate)
            return Result.Failure<AiShotQuoteResponse>(Error.Validation("A scene can only be selected for regeneration."));
        var evaluated = await EvaluateAsync(run, config, request.Regenerate, request.SceneOrder, ct);
        if (evaluated.IsFailure) return Result.Failure<AiShotQuoteResponse>(evaluated.Error);
        var (board, cost, count) = evaluated.Value;
        if (!string.IsNullOrWhiteSpace(request.ExpectedStoryboardFingerprint)
            && !string.Equals(request.ExpectedStoryboardFingerprint, board.Fingerprint, StringComparison.Ordinal))
            return Result.Failure<AiShotQuoteResponse>(Error.Conflict("StoryboardChanged",
                "The prompt preview changed. Review the current prompt before confirming its cost."));
        var expires = time.GetUtcNow().AddMinutes(10);
        var models = string.Join(", ", (board.AiShots ?? []).Select(x => x.Model).Distinct(StringComparer.Ordinal));
        var configFingerprint = WorkflowJson.Hash(WorkflowJson.Write(config));
        return Result.Success(new AiShotQuoteResponse(signer.Sign(new(run.Id, board.Fingerprint, request.Regenerate, cost, expires,
            request.SceneOrder, configFingerprint, board)),
            cost, count, expires, models, "Estimate only. The system MiniMax account is billed for successful generations."));
    }

    public async Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, int? sceneOrder, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Success();
        if (!HasAiVideoScene(config))
            return regenerate
                ? Result.Failure(Error.Validation("Only an AI video scene can be regenerated."))
                : Result.Success();
        var claims = string.IsNullOrWhiteSpace(token) ? null : signer.Read(token);
        if (claims == null || claims.ExpiresAtUtc <= time.GetUtcNow() || claims.RunId != run.Id || claims.Regenerate != regenerate || claims.SceneOrder != sceneOrder)
            return Result.Failure(Error.Validation("Confirm the current AI Shot cost before continuing."));
        var configFingerprint = WorkflowJson.Hash(WorkflowJson.Write(config));
        if (claims.PromptSnapshot is not null && claims.ConfigFingerprint is not null)
        {
            if (!string.Equals(claims.ConfigFingerprint, configFingerprint, StringComparison.Ordinal)
                || !string.Equals(claims.StoryboardFingerprint, claims.PromptSnapshot.Fingerprint, StringComparison.Ordinal))
                return Result.Failure(Error.Conflict("AiShotQuoteChanged", "The reviewed AI prompt snapshot changed. Request a new quote before continuing."));
            var current = await EvaluateAsync(run, config, regenerate, sceneOrder, ct);
            if (current.IsFailure) return Result.Failure(current.Error);
            if (current.Value.Cost > claims.EstimatedCostUsd)
                return Result.Failure(Error.Conflict("AiShotQuoteChanged", "The estimated AI Shot cost increased. Review the updated cost before continuing."));
            if (!SameSourceSnapshot(claims.PromptSnapshot, current.Value.Board))
                return Result.Failure(Error.Conflict("AiShotQuoteChanged", "An approved source image changed. Review the current prompt before continuing."));
            var node = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).Single();
            node.OutputSnapshot = WorkflowJson.Write(new AiShotPromptSnapshot(configFingerprint, claims.PromptSnapshot));
            return Result.Success();
        }

        // Tokens issued before prompt snapshots were introduced remain valid only when their
        // newly planned cost is still within the confirmed maximum.
        var evaluated = await EvaluateAsync(run, config, regenerate, sceneOrder, ct);
        if (evaluated.IsFailure) return Result.Failure(evaluated.Error);
        var (_, cost, _) = evaluated.Value;
        return cost <= claims.EstimatedCostUsd
            ? Result.Success()
            : Result.Failure(Error.Conflict("AiShotQuoteChanged", "The estimated AI Shot cost increased. Review the updated cost before continuing."));
    }

    private static bool HasAiVideoScene(GenerateVideoConfig config) =>
        config.AiScenes is not { Count: > 0 } || config.AiScenes.Any(scene => scene.Mode != "standard");

    private async Task<Result<(Storyboard Board, decimal Cost, int Count)>> EvaluateAsync(WorkflowRun run, GenerateVideoConfig config, bool regenerate, int? sceneOrder, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("Choose AI Shot to request a quote."));
        var planned = await runtime.PlanAsync(run, config, ct);
        if (planned.IsFailure) return Result.Failure<(Storyboard, decimal, int)>(planned.Error);
        var board = planned.Value;
        var node = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).Single();
        var previous = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id, ct)).SingleOrDefault();
        AiShotJobState[] stored = [];
        if ((!regenerate || sceneOrder.HasValue) && previous?.Kind == "render")
        {
            try { stored = WorkflowJson.Read<AiRenderJobPayload>(previous.Payload).AiShotStates?.ToArray() ?? []; }
            catch (System.Text.Json.JsonException) { /* An older Standard job has no AI states. */ }
        }
        var shots = board.AiShots ?? [];
        if (sceneOrder.HasValue && shots.All(x => x.SceneOrder != sceneOrder.Value))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("The selected scene is not part of this storyboard."));
        if (sceneOrder.HasValue && shots.Where(x => x.SceneOrder != sceneOrder.Value).Any(shot => !stored.Any(state =>
                state.Fingerprint == shot.Fingerprint && state.Status == "stored" && !string.IsNullOrEmpty(state.StorageKey) && !string.IsNullOrEmpty(state.StorageVersion))))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Conflict("OtherScenesIncomplete", "Finish or regenerate the other incomplete scenes before replacing this clip."));
        var missing = sceneOrder.HasValue
            ? shots.Where(x => x.SceneOrder == sceneOrder.Value).ToArray()
            : regenerate
                ? shots.ToArray()
                : shots.Where(shot => !stored.Any(state => state.Fingerprint == shot.Fingerprint &&
                    state.Status == "stored" && !string.IsNullOrEmpty(state.StorageKey) && !string.IsNullOrEmpty(state.StorageVersion))).ToArray();
        if (missing.Length > 0)
        {
            foreach (var provider in missing.Select(x => x.ProviderKey).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await videoCredentials.ResolveAsync(run.UserId, provider, ct) is not null) continue;
                var message = string.Equals(provider, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase)
                    ? "AI video is temporarily unavailable because the system MiniMax API key is not configured."
                    : $"Connect a valid {ProviderName(provider)} API key before generating AI video.";
                return Result.Failure<(Storyboard, decimal, int)>(Error.Validation(message));
            }
        }
        return Result.Success((board, missing.Sum(x => x.DurationSeconds * x.PricePerSecondUsd), missing.Length));
    }

    private static string ProviderName(string providerKey) => providerKey.ToLowerInvariant() switch
    {
        "minimax" => "MiniMax",
        "gemini" => "Google Gemini",
        _ => providerKey
    };

    private static bool SameSourceSnapshot(Storyboard quoted, Storyboard current) => quoted.Scenes.Count == current.Scenes.Count
        && quoted.Scenes.Zip(current.Scenes).All(pair => pair.First.MockupId == pair.Second.MockupId
            && pair.First.SourceRevision == pair.Second.SourceRevision
            && string.Equals(pair.First.SourceHash, pair.Second.SourceHash, StringComparison.Ordinal));
}
