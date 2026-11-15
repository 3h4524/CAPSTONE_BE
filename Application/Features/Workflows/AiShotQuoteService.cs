using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// Asks what paid AI video generation would cost: the clips not made yet, to show the seller when they start or change
/// something. The token it returns carries a prompt the seller reviewed; no paid step requires it.
/// </summary>
public sealed record AiShotQuoteRequest(GenerateVideoConfig? Config = null, bool Regenerate = false, int? SceneOrder = null,
    string? ExpectedStoryboardFingerprint = null);
/// <summary>A quote bound to one run, storyboard, and set of missing AI clips.</summary>
/// <param name="ChangesWhatIsMade">
/// The request makes clips the run's own settings did not: other settings than the run's, or a regeneration. The cost
/// of the run's own clips was shown when it started, so the canvas shows this quote only when it is true.
/// </param>
public sealed record AiShotQuoteResponse(string Token, decimal EstimatedCostUsd, int NewClips, DateTimeOffset ExpiresAtUtc,
    string Model, string Note, IReadOnlyList<AiShotQuoteItem>? Items = null, bool ChangesWhatIsMade = false);

/// <summary>One design's part of a batch quote: its new AI clips, how long they are together, and their cost.</summary>
public sealed record AiShotQuoteItem(string Label, int Clips, decimal Seconds, decimal EstimatedCostUsd);

/// <summary>
/// One design of a batch video run, before its run exists: the run it would get, the settings resolved for it, and
/// the state of the images those settings name.
/// </summary>
/// <param name="Label">The design in a message, as "<c>Product · Design 1</c>".</param>
/// <param name="SourceFingerprint">The design's approved images with their revisions, so a changed image voids the quote.</param>
public sealed record PlannedVideo(WorkflowRun Run, GenerateVideoConfig Config, string Label, string SourceFingerprint);

public interface IAiShotQuoteService
{
    Task<Result<AiShotQuoteResponse>> QuoteAsync(Guid runId, AiShotQuoteRequest request, CancellationToken ct);

    /// <summary>
    /// Checks that these AI clips can be made (the plan includes AI video, the provider has a key, the scenes resolve).
    /// The cost was shown when the seller started or changed the run, so no confirmation token is required; a token
    /// that still matches carries the prompt the seller reviewed into the render.
    /// </summary>
    Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, int? sceneOrder, CancellationToken ct);

    /// <summary>What every AI clip of a batch video run costs together, quoted once for the whole group.</summary>
    Task<Result<AiShotQuoteResponse>> QuoteBatchAsync(Guid groupId, IReadOnlyList<PlannedVideo> plans, CancellationToken ct);

    /// <summary>Checks that the plan includes AI video when these designs make AI clips; no confirmation token is required.</summary>
    Task<Result> VerifyBatchAsync(Guid groupId, IReadOnlyList<PlannedVideo> plans, string? token, CancellationToken ct);
}

public sealed class AiShotQuoteService(ICurrentUser user, IRepository<WorkflowRun> runs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MediaJob> jobs, IAiVideoCredentialResolver videoCredentials, WorkflowRuntime runtime, IAiShotQuoteSigner signer,
    TimeProvider time, IPlanEntitlementService entitlements, IRunTargetConfigResolver? targetResolver = null) : IAiShotQuoteService
{
    public async Task<Result<AiShotQuoteResponse>> QuoteAsync(Guid runId, AiShotQuoteRequest request, CancellationToken ct)
    {
        var run = await runs.GetByIdAsync(runId, ct);
        if (run == null || run.UserId != user.UserId) return Result.Failure<AiShotQuoteResponse>(Error.NotFound("RunNotFound", "Run not found."));
        var reopenCancelledScene = run.Status == "cancelled" && request.Regenerate && request.SceneOrder.HasValue;
        // A stopped run is quoted for what going on with it would make: the clips it has not stored yet.
        var resumeStopped = run.Status == "cancelled" && !request.Regenerate;
        if (run.Status is not ("waiting_for_input" or "waiting_for_review" or "failed") && !reopenCancelledScene && !resumeStopped)
            return Result.Failure<AiShotQuoteResponse>(Error.Conflict("InvalidCheckpoint", "The run is not ready for AI Shot confirmation."));
        var node = (await nodes.FindAsync(x => x.WorkflowRunId == runId && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (node is null || (reopenCancelledScene && node.Status != "succeeded"))
            return Result.Failure<AiShotQuoteResponse>(Error.Conflict("InvalidCheckpoint", "The run is not ready for AI Shot confirmation."));
        var config = request.Config ?? WorkflowJson.Read<GenerateVideoConfig>(node.InputSnapshot);
        // A run of a batch makes one design's video: settings sent from the canvas name the preview product's images.
        if (request.Config != null && run.RunGroupId != null && targetResolver != null)
        {
            var resolved = await targetResolver.ResolveForRunAsync(run, request.Config, ct);
            if (resolved.Error != null) return Result.Failure<AiShotQuoteResponse>(Error.Validation(resolved.Error));
            config = resolved.Config;
        }
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
        var models = string.Join(", ", (board.AiShots ?? [])
            .Where(x => !request.SceneOrder.HasValue || x.SceneOrder == request.SceneOrder.Value)
            .Select(x => x.Model).Distinct(StringComparer.Ordinal));
        var configFingerprint = WorkflowJson.Hash(WorkflowJson.Write(config));
        var changesWhatIsMade = request.Regenerate
            || WorkflowJson.Write(config) != WorkflowJson.Write(WorkflowJson.Read<GenerateVideoConfig>(node.InputSnapshot));
        return Result.Success(new AiShotQuoteResponse(signer.Sign(new(run.Id, board.Fingerprint, request.Regenerate, cost, expires,
            request.SceneOrder, configFingerprint, board)),
            cost, count, expires, models, "Estimate only. The system MiniMax account is billed for successful generations.",
            ChangesWhatIsMade: changesWhatIsMade));
    }

    public async Task<Result> VerifyAsync(WorkflowRun run, GenerateVideoConfig config, string? token, bool regenerate, int? sceneOrder, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Success();
        if (!HasAiVideoScene(config))
            return regenerate
                ? Result.Failure(Error.Validation("Only an AI video scene can be regenerated."))
                : Result.Success();
        // The seller saw the cost of the clips still to make when they started the run or changed it: what is left is
        // made without a second confirmation. This still refuses what cannot be made (no AI video on the plan, no key).
        var current = await EvaluateAsync(run, config, regenerate, sceneOrder, ct);
        if (current.IsFailure) return Result.Failure(current.Error);

        // A prompt the seller reviewed before rendering is the one rendered, while it still matches these settings and images.
        var claims = string.IsNullOrWhiteSpace(token) ? null : signer.Read(token);
        var configFingerprint = WorkflowJson.Hash(WorkflowJson.Write(config));
        if (claims is { PromptSnapshot: not null } && claims.ExpiresAtUtc > time.GetUtcNow() && claims.RunId == run.Id
            && claims.Regenerate == regenerate && claims.SceneOrder == sceneOrder
            && string.Equals(claims.ConfigFingerprint, configFingerprint, StringComparison.Ordinal)
            && string.Equals(claims.StoryboardFingerprint, claims.PromptSnapshot.Fingerprint, StringComparison.Ordinal)
            && SameSourceSnapshot(claims.PromptSnapshot, current.Value.Board))
        {
            var node = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).Single();
            node.OutputSnapshot = WorkflowJson.Write(new AiShotPromptSnapshot(configFingerprint, claims.PromptSnapshot));
        }
        return Result.Success();
    }

    public async Task<Result<AiShotQuoteResponse>> QuoteBatchAsync(Guid groupId, IReadOnlyList<PlannedVideo> plans, CancellationToken ct)
    {
        var paid = plans.Where(plan => NeedsConsent(plan.Config)).ToArray();
        if (paid.Length == 0)
            return Result.Failure<AiShotQuoteResponse>(Error.Validation("No design of this run makes an AI video clip."));
        var planCheck = await entitlements.RequireFeatureAsync(paid[0].Run.UserId, PlanFeatures.AiVideo, ct);
        if (planCheck.IsFailure) return Result.Failure<AiShotQuoteResponse>(planCheck.Error);

        var cost = 0m;
        var clips = 0;
        var models = new List<string>();
        var items = new List<AiShotQuoteItem>(paid.Length);
        foreach (var plan in paid)
        {
            // The run does not exist yet, so this plans the design's storyboard exactly as its run would.
            var planned = await runtime.PlanAsync(plan.Run, plan.Config, ct);
            if (planned.IsFailure)
                return Result.Failure<AiShotQuoteResponse>(Error.Validation($"{plan.Label}: {planned.Error.Message}"));
            var shots = planned.Value.AiShots ?? [];
            foreach (var provider in shots.Select(shot => shot.ProviderKey).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await videoCredentials.ResolveAsync(plan.Run.UserId, provider, ct) is not null) continue;
                return Result.Failure<AiShotQuoteResponse>(Error.Validation(string.Equals(provider, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase)
                    ? "AI video is temporarily unavailable because the system MiniMax API key is not configured."
                    : $"Connect a valid {ProviderName(provider)} API key before generating AI video."));
            }
            var designCost = shots.Sum(shot => shot.DurationSeconds * shot.PricePerSecondUsd);
            items.Add(new(plan.Label, shots.Count, shots.Sum(shot => (decimal)shot.DurationSeconds), designCost));
            cost += designCost;
            clips += shots.Count;
            models.AddRange(shots.Select(shot => shot.Model));
        }

        var fingerprint = BatchFingerprint(plans);
        var expires = time.GetUtcNow().AddMinutes(10);
        return Result.Success(new AiShotQuoteResponse(
            signer.Sign(new(groupId, fingerprint, false, cost, expires, null, fingerprint, null)),
            cost, clips, expires, string.Join(", ", models.Distinct(StringComparer.Ordinal)),
            $"Estimate only for {paid.Length} video{(paid.Length == 1 ? "" : "s")}. The system MiniMax account is billed for successful generations.", items));
    }

    public async Task<Result> VerifyBatchAsync(Guid groupId, IReadOnlyList<PlannedVideo> plans, string? token, CancellationToken ct)
    {
        // The seller saw what the AI clips cost when pressing Run: the videos start without a second confirmation.
        var paid = plans.FirstOrDefault(plan => NeedsConsent(plan.Config));
        return paid is null ? Result.Success() : await entitlements.RequireFeatureAsync(paid.Run.UserId, PlanFeatures.AiVideo, ct);
    }

    /// <summary>What a quoted cost is bound to: every design that gets a video, with its settings and its images.</summary>
    private static string BatchFingerprint(IReadOnlyList<PlannedVideo> plans) => WorkflowJson.Hash(WorkflowJson.Write(plans
        .Select(plan => new { plan.Run.ProductId, plan.Run.ArtworkGroupKey, plan.Config, plan.SourceFingerprint })
        .OrderBy(plan => plan.ProductId).ThenBy(plan => plan.ArtworkGroupKey, StringComparer.Ordinal).ToArray()));

    /// <summary>Whether these settings make paid AI clips, so the seller has to confirm their cost.</summary>
    private static bool NeedsConsent(GenerateVideoConfig config) => config.Mode == "ai_shot" && HasAiVideoScene(config);

    private static bool HasAiVideoScene(GenerateVideoConfig config) =>
        config.AiScenes is not { Count: > 0 } || config.AiScenes.Any(scene => scene.Mode != "standard");

    private async Task<Result<(Storyboard Board, decimal Cost, int Count)>> EvaluateAsync(WorkflowRun run, GenerateVideoConfig config, bool regenerate, int? sceneOrder, CancellationToken ct)
    {
        if (config.Mode != "ai_shot") return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("Choose AI Shot to request a quote."));
        // Every paid AI video action is quoted or verified here, so this one check gates them all.
        var planCheck = await entitlements.RequireFeatureAsync(run.UserId, PlanFeatures.AiVideo, ct);
        if (planCheck.IsFailure) return Result.Failure<(Storyboard, decimal, int)>(planCheck.Error);
        var planned = await runtime.PlanAsync(run, config, ct);
        if (planned.IsFailure) return Result.Failure<(Storyboard, decimal, int)>(planned.Error);
        var board = planned.Value;
        var node = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).Single();
        var previous = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id && x.Kind == "render", ct)).SingleOrDefault();
        AiShotJobState[] stored = [];
        AiShotPlan[] previousShots = [];
        if ((!regenerate || sceneOrder.HasValue) && previous is not null)
        {
            try
            {
                var payload = WorkflowJson.Read<AiRenderJobPayload>(previous.Payload);
                stored = payload.AiShotStates?.ToArray() ?? [];
                previousShots = payload.Storyboard?.AiShots?.ToArray() ?? [];
            }
            catch (System.Text.Json.JsonException) { /* An older Standard job has no AI states. */ }
        }
        var shots = board.AiShots ?? [];
        if (sceneOrder.HasValue && shots.All(x => x.SceneOrder != sceneOrder.Value))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Validation("The selected scene is not part of this storyboard."));
        if (run.Status == "cancelled" && sceneOrder.HasValue &&
            (!previousShots.Any(shot => shot.SceneOrder == sceneOrder.Value) || !stored.Any(state =>
                state.SceneOrder == sceneOrder.Value && state.Status == "stored" &&
                !string.IsNullOrEmpty(state.StorageKey) && !string.IsNullOrEmpty(state.StorageVersion))))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Conflict("SceneNotStored", "The selected scene has no stored clip to regenerate."));
        // Only for replacing one clip of a stopped run; going on with a stopped run makes whatever it has not stored.
        if (run.Status == "cancelled" && sceneOrder.HasValue && previousShots.Any(shot => stored.All(state => state.SceneOrder != shot.SceneOrder ||
                state.Fingerprint != shot.Fingerprint || state.Status != "stored" ||
                string.IsNullOrEmpty(state.StorageKey) || string.IsNullOrEmpty(state.StorageVersion))))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Conflict("OtherScenesIncomplete", "Finish the other AI scenes before replacing this clip."));
        if (sceneOrder.HasValue && shots.Where(x => x.SceneOrder != sceneOrder.Value).Any(shot =>
            previousShots.FirstOrDefault(old => old.SceneOrder == shot.SceneOrder) is { } old &&
            (!string.Equals(old.ProviderKey, shot.ProviderKey, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(old.ModelKey, shot.ModelKey, StringComparison.OrdinalIgnoreCase))))
            return Result.Failure<(Storyboard, decimal, int)>(Error.Conflict("AiVideoOptionChanged",
                "Another scene's AI video option has changed. Generate that scene separately or regenerate all AI scenes together."));
        if (sceneOrder.HasValue && shots.Where(x => x.SceneOrder != sceneOrder.Value).Any(shot => !stored.Any(state =>
                state.SceneOrder == shot.SceneOrder && state.Status == "stored" && !string.IsNullOrEmpty(state.StorageKey) && !string.IsNullOrEmpty(state.StorageVersion))))
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
