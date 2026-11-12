using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowRunGroupService
{
    Task<Result<RunGroupResponse>> StartAsync(Guid workflowId, StartRunGroupRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> GetAsync(Guid groupId, CancellationToken ct);
    Task<Result<IReadOnlyList<RunGroupSummaryResponse>>> ListAsync(Guid workflowId, CancellationToken ct);
    Task<Result<RunGroupResponse>> UpdateOverrideAsync(Guid groupId, UpdateTargetOverrideRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> UpdateConfigAsync(Guid groupId, UpdateTargetConfigRequest request, CancellationToken ct);

    /// <summary>What the AI clips of every design cost together, confirmed once before the videos start.</summary>
    Task<Result<AiShotQuoteResponse>> QuoteAiAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> ConfirmAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> CancelAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);
}

/// <summary>
/// The video part of a batch run: one video per (product, design), from one scene list.
/// </summary>
/// <remarks>
/// A group starts once the designs and their mock-ups exist and waits at Mockup Approval. A design whose mock-ups
/// the seller approves there gets a video. Confirming creates one ordinary workflow run per such design (the child
/// runs), already past Mockup Approval and with the scene list rewritten to the design's own mock-ups; the
/// background planner then plans and queues each one. From there a child run is retried, reviewed and exported on
/// its own, and one that fails does not hold up the others.
/// </remarks>
public sealed class WorkflowRunGroupService(ICurrentUser user, IRepository<Workflow> workflows, IRepository<WorkflowRunGroup> groups,
    IRepository<WorkflowRun> runs, IRepository<WorkflowNodeRun> nodes, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<BatchJob> batchJobs, IRepository<BatchJobProduct> jobProducts, IRepository<DesignImage> designs,
    IRepository<MediaJob> mediaJobs, IWorkflowStateRepository state, IUnitOfWork uow, WorkflowGraphValidator graph, IRunTargetConfigResolver targetConfigs,
    IPlanEntitlementService entitlements, IMediaStorage storage, TimeProvider time,
    IRunGroupPlannerQueue? planner = null, IWorkflowRunNotifier? notifier = null,
    Validators.GenerateVideoConfigValidator? videoValidator = null, IAiShotQuoteService? quotes = null,
    ILifestyleMockupService? lifestyle = null, IRepository<MockupTemplate>? mockupTemplates = null) : IWorkflowRunGroupService
{
    /// <summary>Videos one group makes at most.</summary>
    public const int MaximumTargets = 10;

    private static readonly string[] FinishedRun = ["completed", "failed", "cancelled"];

    public async Task<Result<RunGroupResponse>> StartAsync(Guid workflowId, StartRunGroupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return Result.Failure<RunGroupResponse>(Error.Validation("Provide an idempotency key of 1–100 characters."));
        if (request.AiBudgetUsd is < 0)
            return Result.Failure<RunGroupResponse>(Error.Validation("The AI budget cannot be negative."));
        var userId = user.UserId!.Value;
        await using var tx = await uow.BeginTransactionAsync(ct);
        await state.LockOwnerAsync(userId, ct);
        var requestHash = WorkflowJson.Hash(WorkflowJson.Write(new { workflowId, request.ExpectedWorkflowRevision, request.BatchJobId }));
        var existing = (await groups.FindAsync(x => x.UserId == userId && x.IdempotencyKey == request.IdempotencyKey, ct)).SingleOrDefault();
        if (existing != null)
            return existing.RequestHash == requestHash
                ? Result.Success(await MapAsync(existing, ct))
                : Result.Failure<RunGroupResponse>(Error.Conflict("IdempotencyConflict", "Key was used for another request."));

        var workflow = await workflows.GetByIdAsync(workflowId, ct);
        if (workflow is null || workflow.UserId != userId || workflow.DeletedAt != null) return Result.Failure<RunGroupResponse>(WorkflowMissing());
        if (workflow.Revision != request.ExpectedWorkflowRevision) return Result.Failure<RunGroupResponse>(Stale());
        var definition = WorkflowJson.Read<WorkflowDefinition>(workflow.Definition);
        var error = graph.Validate(definition, requireProduct: false);
        if (error != null) return Result.Failure<RunGroupResponse>(error);
        if (definition.Nodes.SingleOrDefault(x => x.Type == "generate-video") is not { } videoNode)
            return Result.Failure<RunGroupResponse>(Error.Validation("Add Generate Video to make a video for each design."));
        var config = WorkflowJson.Config<GenerateVideoConfig>(videoNode);
        if (!WorkflowGraphValidator.SupportsBatchVideos(config))
            return Result.Failure<RunGroupResponse>(Error.Validation(
                "These video settings make one product's video only. Set up the scenes of the video, and leave the Product on Model pack to a single product."));

        var batchId = WorkflowGraphValidator.BatchIdOf(definition);
        if (request.BatchJobId is Guid jobId)
        {
            var job = await batchJobs.GetByIdAsync(jobId, ct);
            if (job is null || job.DeletedAt != null || job.UserId != userId)
                return Result.Failure<RunGroupResponse>(Error.NotFound("BatchJobNotFound", "Design job not found."));
            if (job.BatchId != batchId)
                return Result.Failure<RunGroupResponse>(Error.Validation("The design job belongs to another batch than Product Input."));
        }

        var fullRun = await entitlements.RequireAllowanceAsync(userId, UsageAllowances.FullRuns, cancellationToken: ct);
        if (fullRun.IsFailure) return Result.Failure<RunGroupResponse>(fullRun.Error);
        var videoCheck = await VideoPlanChecks.CheckAsync(entitlements, userId, config, 1, ct);
        if (videoCheck.IsFailure) return Result.Failure<RunGroupResponse>(videoCheck.Error);

        var now = time.GetUtcNow().UtcDateTime;
        var group = new WorkflowRunGroup
        {
            Id = Guid.NewGuid(), WorkflowId = workflowId, UserId = userId, BatchId = batchId, BatchJobId = request.BatchJobId,
            WorkflowRevision = workflow.Revision, DefinitionSnapshot = workflow.Definition, TargetOverrides = "{}",
            Status = "waiting_for_targets", Revision = 1, IdempotencyKey = request.IdempotencyKey, RequestHash = requestHash,
            CreatedAt = now, UpdatedAt = now
        };
        await groups.AddAsync(group, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notifier != null) await notifier.GroupChangedAsync(group.Id, ct);
        // The model photos are made by Ideogram and paid for, so only after the seller confirmed the Run's cost, which lists them.
        if (request.AiBudgetUsd is > 0) await MakeModelPhotosAsync(group, ct);
        if (MockupAutoApproval.IsAuto(definition)) return Result.Success(await AutoConfirmAsync(group, request.AiBudgetUsd, ct));
        return Result.Success(await MapAsync(group, ct));
    }

    /// <summary>
    /// Mockup Approval in auto mode: approves every design's mock-ups waiting for review and starts the videos at once.
    /// AI clips start only within <paramref name="aiBudgetUsd"/>, the AI cost the seller confirmed when pressing Run:
    /// their exact quote is taken here and stands for the consent. When they cannot start (a quote over that budget or
    /// none given, too many designs, no video left on the plan...), the group waits at Mockup Approval as in manual
    /// mode, with the mock-ups already approved.
    /// </summary>
    private async Task<RunGroupResponse> AutoConfirmAsync(WorkflowRunGroup group, decimal? aiBudgetUsd, CancellationToken ct)
    {
        await ApproveWaitingAsync(group, ct);
        string? token = null;
        if (aiBudgetUsd is decimal allowed && quotes is not null)
        {
            var quote = await QuoteAiAsync(group.Id, new(group.Revision), ct);
            if (quote.IsSuccess && quote.Value.EstimatedCostUsd <= allowed) token = quote.Value.Token;
        }
        var confirmed = await ConfirmAsync(group.Id, new(group.Revision, token), ct);
        return confirmed.IsSuccess ? confirmed.Value : await MapAsync(group, ct);
    }

    /// <summary>Approves, as the owner, every stored mock-up of the group's products still waiting for review.</summary>
    private async Task ApproveWaitingAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var scope = (await ScopeAsync(group, ct)).ToArray();
        var stored = await assets.FindAsync(x => scope.Contains(x.ProductId) && x.DeletedAt == null && x.ContentHash != null, ct);
        if (MockupAutoApproval.Approve(stored, group.UserId, time.GetUtcNow().UtcDateTime) > 0) await uow.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Makes the model photo of every design whose scenes show a person and that has none (<see cref="PersonModelPhotos"/>):
    /// Ideogram dresses the system's model photo for the product type in the design's main mock-up, as an AI lifestyle
    /// image is made at Apply mock-up. A design whose scenes need no person, or that has a model photo already, is left
    /// alone; one that cannot be made (no Ideogram key, no system model, no mock-up) keeps the error that asks the seller
    /// for one. Returns how many were made; they wait for review.
    /// </summary>
    private async Task<int> MakeModelPhotosAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        if (lifestyle is null || mockupTemplates is null) return 0;
        var (_, shared) = await SharedConfigAsync(group, ct);
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        var needing = (await TargetsAsync(group, ct))
            .Where(target => PersonModelPhotos.Needs(ConfigFor(shared, overrides.GetValueOrDefault(target.Key), target.Product.Id)) && !PersonModelPhotos.Has(target.Mockups))
            .ToArray();
        if (needing.Length == 0) return 0;

        var models = await mockupTemplates.FindAsync(x => x.UserId == null && x.Kind == MockupRules.ModelKind && x.IsActive == true, ct);
        var made = 0;
        foreach (var target in needing)
        {
            if (PersonModelPhotos.DefaultModel(models, target.Product.ProductType) is not { } model) continue;
            // One paid attempt, from the main mock-up: a failure is not tried again with another one at the seller's cost.
            if (PersonModelPhotos.Garments(target.Mockups).FirstOrDefault() is not { } garment) continue;
            if ((await lifestyle.GenerateForVideoAsync(target.Product.Id, garment.Id, model.Id, ct)).IsSuccess) made++;
        }
        return made;
    }

    public async Task<Result<RunGroupResponse>> GetAsync(Guid groupId, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        return Result.Success(await MapAsync(group, ct));
    }

    public async Task<Result<IReadOnlyList<RunGroupSummaryResponse>>> ListAsync(Guid workflowId, CancellationToken ct)
    {
        var owned = (await groups.FindAsync(x => x.WorkflowId == workflowId && x.UserId == user.UserId, ct))
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(20).ToArray();
        if (owned.Length == 0) return Result.Success<IReadOnlyList<RunGroupSummaryResponse>>([]);
        var ids = owned.Select(x => x.Id).ToArray();
        var children = (await runs.FindAsync(x => x.RunGroupId.HasValue && ids.Contains(x.RunGroupId.Value), ct))
            .GroupBy(x => x.RunGroupId!.Value).ToDictionary(x => x.Key, x => x.ToArray());
        return Result.Success<IReadOnlyList<RunGroupSummaryResponse>>(owned.Select(group =>
        {
            var own = children.GetValueOrDefault(group.Id) ?? [];
            return new RunGroupSummaryResponse(group.Id, StatusOf(group, own), group.CreatedAt, own.Length, own.Count(x => x.Status == "completed"), group.BatchJobId);
        }).ToArray());
    }

    public async Task<Result<RunGroupResponse>> UpdateOverrideAsync(Guid groupId, UpdateTargetOverrideRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ArtworkGroupKey) || request.ArtworkGroupKey.Length > 100)
            return Result.Failure<RunGroupResponse>(Error.Validation("Choose a design."));
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<RunGroupResponse>(Error.Conflict("TargetsConfirmed", "The videos have started: change a design's scenes from its own run."));
        if (!(await ScopeAsync(group, ct)).Contains(request.ProductId))
            return Result.Failure<RunGroupResponse>(Error.NotFound("ProductNotFound", "Product not found in this run."));

        var scenes = (request.Scenes ?? []).GroupBy(x => x.SceneIndex).Select(x => x.Last()).OrderBy(x => x.SceneIndex).ToArray();
        if (scenes.Any(x => x.SceneIndex is < 0 or > 3))
            return Result.Failure<RunGroupResponse>(Error.Validation("A video has at most four scenes."));
        if (scenes.Length > 0)
        {
            var approved = (await targetConfigs.ApprovedAsync(request.ProductId, request.ArtworkGroupKey, ct)).Select(x => x.Id).ToHashSet();
            if (scenes.Any(x => !approved.Contains(x.MockupId)))
                return Result.Failure<RunGroupResponse>(Error.Validation("Pick an approved mock-up of this design for each scene."));
        }
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides).ToDictionary(x => x.Key, x => x.Value);
        var key = RunTargetConfigResolver.TargetKey(request.ProductId, request.ArtworkGroupKey);
        // The design's own settings, if it has them, stay: only its scene picks change here.
        Store(overrides, key, new TargetOverride(scenes, overrides.GetValueOrDefault(key)?.Config));
        return await SaveOverridesAsync(group, overrides, tx, ct);
    }

    /// <summary>
    /// Gives one design Generate Video settings of its own (scene list, duration, format...), or puts it back on the
    /// workflow's shared ones. Its images are named as the design's own mock-ups; the scene picks it has stay.
    /// </summary>
    public async Task<Result<RunGroupResponse>> UpdateConfigAsync(Guid groupId, UpdateTargetConfigRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ArtworkGroupKey) || request.ArtworkGroupKey.Length > 100)
            return Result.Failure<RunGroupResponse>(Error.Validation("Choose a design."));
        if (request.Config is { } config)
        {
            // Every design of a batch run renders from its own scene list, so settings that fit one product only are refused.
            if (!WorkflowGraphValidator.SupportsBatchVideos(config))
                return Result.Failure<RunGroupResponse>(Error.Validation("A design's own settings have to be a scene list, without the Product on Model pack."));
            if (videoValidator != null && await videoValidator.ValidateForBatchAsync(config, ct) is { IsValid: false } invalid)
                return Result.Failure<RunGroupResponse>(Error.Validation(string.Join(" ", invalid.Errors.Select(x => x.ErrorMessage))));
        }
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<RunGroupResponse>(Error.Conflict("TargetsConfirmed", "The videos have started: change a design's video from its own run."));
        if (!(await ScopeAsync(group, ct)).Contains(request.ProductId))
            return Result.Failure<RunGroupResponse>(Error.NotFound("ProductNotFound", "Product not found in this run."));

        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides).ToDictionary(x => x.Key, x => x.Value);
        var key = RunTargetConfigResolver.TargetKey(request.ProductId, request.ArtworkGroupKey);
        Store(overrides, key, new TargetOverride(overrides.GetValueOrDefault(key)?.Scenes ?? [], request.Config));
        return await SaveOverridesAsync(group, overrides, tx, ct);
    }

    /// <summary>An override with neither scene picks nor settings of its own is the shared configuration: it is dropped.</summary>
    private static void Store(Dictionary<string, TargetOverride> overrides, string key, TargetOverride next)
    {
        if (next.Scenes.Count == 0 && next.Config is null) overrides.Remove(key);
        else overrides[key] = next;
    }

    private async Task<Result<RunGroupResponse>> SaveOverridesAsync(WorkflowRunGroup group, Dictionary<string, TargetOverride> overrides,
        IUnitOfWorkTransaction tx, CancellationToken ct)
    {
        group.TargetOverrides = WorkflowJson.Write(overrides);
        group.Revision++; group.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notifier != null) await notifier.GroupChangedAsync(group.Id, ct);
        return Result.Success(await MapAsync(group, ct));
    }

    /// <summary>The settings a design's run renders with: its Generate Video step's input, which a re-render replaces.</summary>
    private static GenerateVideoConfig? RunConfig(IReadOnlyCollection<WorkflowNodeRun>? steps)
    {
        if (steps?.FirstOrDefault(step => step.NodeType == "generate-video") is not { InputSnapshot: { Length: > 2 } input }) return null;
        try { return WorkflowJson.Read<GenerateVideoConfig>(input); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The settings one design renders with: its own (set at Mockup Approval), else its product's (set before the run),
    /// else the workflow's shared ones.
    /// </summary>
    private static GenerateVideoConfig ConfigFor(GenerateVideoConfig shared, TargetOverride? picked, Guid productId) =>
        picked?.Config ?? shared.ForProduct(productId);

    /// <summary>The settings the product was given before the run, if any: what its designs show until given their own.</summary>
    private static GenerateVideoConfig? ProductOwn(GenerateVideoConfig shared, Guid productId) =>
        shared.ProductConfigs?.ContainsKey(productId) == true ? shared.ForProduct(productId) : null;

    /// <summary>One design that gets a video: its mock-ups, the settings resolved for it, and the run it will get.</summary>
    private sealed record TargetPlan(Target Target, GenerateVideoConfig Config, WorkflowDefinition Child, WorkflowRun Run);

    /// <summary>
    /// The videos a run group would make now. The AI quote plans these runs before they exist and Confirm stores
    /// them, so the cost the seller confirms belongs to exactly the designs and settings that then render.
    /// </summary>
    private async Task<Result<IReadOnlyList<TargetPlan>>> PlanTargetsAsync(WorkflowRunGroup group, WorkflowDefinition definition,
        GenerateVideoConfig shared, DateTime now, CancellationToken ct)
    {
        var targets = (await TargetsAsync(group, ct)).Where(x => x.Approved.Count > 0).ToArray();
        if (targets.Length == 0)
            return Result.Failure<IReadOnlyList<TargetPlan>>(Error.Conflict("WaitingForInput", "Approve at least one mock-up of the designs that should get a video."));
        if (targets.Length > MaximumTargets)
            return Result.Failure<IReadOnlyList<TargetPlan>>(Error.Validation(
                $"{targets.Length} designs have approved mock-ups; one run makes at most {MaximumTargets} videos. Reject the mock-ups of the designs to leave out."));

        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        var plans = new List<TargetPlan>(targets.Length);
        var problems = new List<string>();
        foreach (var target in targets)
        {
            overrides.TryGetValue(target.Key, out var picked);
            var resolved = await targetConfigs.ResolveAsync(group.UserId, target.Product.Id, target.ArtworkGroupKey, ConfigFor(shared, picked, target.Product.Id), picked, ct);
            if (resolved.Error != null) { problems.Add($"{Label(target)}: {resolved.Error}"); continue; }
            var child = ForTarget(definition, target.Product.Id, target.ArtworkGroupKey, resolved.Config);
            plans.Add(new(target, resolved.Config, child, ChildRun(group, child, target, now, plans.Count)));
        }
        if (problems.Count > 0)
            return Result.Failure<IReadOnlyList<TargetPlan>>(Error.Validation(string.Join(" ", problems) + " Reject that design's mock-ups to leave it out."));
        return Result.Success<IReadOnlyList<TargetPlan>>(plans);
    }

    private static string Label(Target target) => $"{target.Product.Name} · {target.DesignLabel}";

    private static WorkflowRun ChildRun(WorkflowRunGroup group, WorkflowDefinition child, Target target, DateTime now, int index) => new()
    {
        Id = Guid.NewGuid(), WorkflowId = group.WorkflowId, UserId = group.UserId, ProductId = target.Product.Id,
        RunGroupId = group.Id, ArtworkGroupKey = target.ArtworkGroupKey, WorkflowRevision = group.WorkflowRevision,
        DefinitionSnapshot = WorkflowJson.Write(child), Status = "queued", Revision = 1,
        IdempotencyKey = $"group:{group.Id:N}:{index}", RequestHash = group.RequestHash,
        CreatedAt = now.AddTicks(index), UpdatedAt = now
    };

    private static IReadOnlyList<PlannedVideo> ForQuote(IReadOnlyList<TargetPlan> plans) => plans
        .Select(plan => new PlannedVideo(plan.Run, plan.Config, Label(plan.Target), SourceFingerprint(plan.Target))).ToArray();

    /// <summary>The design's images with their revisions, so a re-made or newly approved image voids a confirmed cost.</summary>
    private static string SourceFingerprint(Target target) => WorkflowJson.Hash(WorkflowJson.Write(target.Approved
        .Concat(target.Frames).OrderBy(asset => asset.Id)
        .Select(asset => new { asset.Id, asset.MetadataRevision, asset.ContentHash }).ToArray()));

    public async Task<Result<AiShotQuoteResponse>> QuoteAiAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        if (quotes is null) return Result.Failure<AiShotQuoteResponse>(Error.Validation("AI video is not available."));
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<AiShotQuoteResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<AiShotQuoteResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<AiShotQuoteResponse>(Error.Conflict("TargetsConfirmed", "The videos of this run have already started."));
        var (definition, config) = await SharedConfigAsync(group, ct);
        var plans = await PlanTargetsAsync(group, definition, config, time.GetUtcNow().UtcDateTime, ct);
        return plans.IsFailure
            ? Result.Failure<AiShotQuoteResponse>(plans.Error)
            : await quotes.QuoteBatchAsync(group.Id, ForQuote(plans.Value), ct);
    }

    public async Task<Result<RunGroupResponse>> ConfirmAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<RunGroupResponse>(Error.Conflict("TargetsConfirmed", "The videos of this run have already started."));

        var (definition, config) = await SharedConfigAsync(group, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var planned = await PlanTargetsAsync(group, definition, config, now, ct);
        if (planned.IsFailure) return Result.Failure<RunGroupResponse>(planned.Error);
        var plans = planned.Value;

        var fullRun = await entitlements.RequireAllowanceAsync(group.UserId, UsageAllowances.FullRuns, cancellationToken: ct);
        if (fullRun.IsFailure) return Result.Failure<RunGroupResponse>(fullRun.Error);
        var videoCheck = await VideoPlanChecks.CheckAsync(entitlements, group.UserId, config, plans.Count, ct);
        if (videoCheck.IsFailure) return Result.Failure<RunGroupResponse>(videoCheck.Error);
        // A design or product with settings of its own may need a feature the shared ones do not (landscape output).
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        foreach (var own in plans.Select(plan => ConfigFor(config, overrides.GetValueOrDefault(plan.Target.Key), plan.Target.Product.Id)).Distinct())
        {
            var ownCheck = await VideoPlanChecks.CheckFeaturesAsync(entitlements, group.UserId, own, ct);
            if (ownCheck.IsFailure) return Result.Failure<RunGroupResponse>(ownCheck.Error);
        }
        // AI clips are billed per second, so the seller confirms what the whole run costs before any of it is rendered.
        if (quotes is not null)
        {
            var consent = await quotes.VerifyBatchAsync(group.Id, ForQuote(plans), request.AiShotQuoteToken, ct);
            if (consent.IsFailure) return Result.Failure<RunGroupResponse>(consent.Error);
        }

        foreach (var plan in plans)
        {
            await runs.AddAsync(plan.Run, cancellationToken: ct);
            foreach (var node in plan.Child.Nodes)
                await nodes.AddAsync(ChildNode(plan.Run, node, plan.Target, now), cancellationToken: ct);
        }
        // The group keeps the scene list its videos were made with.
        group.DefinitionSnapshot = WorkflowJson.Write(definition with
        {
            Nodes = definition.Nodes.Select(node => node.Type == "generate-video"
                ? node with { Config = JsonSerializer.SerializeToElement(config, WorkflowJson.Options) } : node).ToArray()
        });
        group.Status = "running"; group.Revision++; group.UpdatedAt = now; group.ErrorMessage = null;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        planner?.Signal();
        if (notifier != null) await notifier.GroupChangedAsync(group.Id, ct);
        return Result.Success(await MapAsync(group, ct));
    }

    public async Task<Result<RunGroupResponse>> CancelAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status is "completed" or "cancelled")
            return Result.Failure<RunGroupResponse>(Error.Conflict("RunFinished", "This run is already finished."));
        var now = time.GetUtcNow().UtcDateTime;
        var changed = new List<WorkflowRun>();
        foreach (var child in await runs.FindAsync(x => x.RunGroupId == group.Id, ct))
        {
            if (FinishedRun.Contains(child.Status)) continue;
            // Locked one by one, as an action on the run would: the planner or a worker may hold it.
            var run = await state.LockRunAsync(child.Id, ct);
            if (run is null || FinishedRun.Contains(run.Status)) continue;
            run.Status = "cancelled"; run.CompletedAt = now; run.Revision++; run.UpdatedAt = now;
            foreach (var step in await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.Status != "succeeded", ct)) step.Status = "cancelled";
            changed.Add(run);
        }
        // A job of a cancelled run is no longer leased or claimed: the worker's next heartbeat finds the run cancelled.
        foreach (var run in changed)
            foreach (var job in await mediaJobs.FindAsync(x => x.WorkflowRunId == run.Id && (x.Status == "queued" || x.Status == "leased"), ct))
            { job.Status = "cancelled"; job.LeaseToken = null; }
        group.Status = "cancelled"; group.CompletedAt = now; group.Revision++; group.UpdatedAt = now;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notifier != null)
        {
            foreach (var run in changed) await notifier.RunChangedAsync(run, ct);
            await notifier.GroupChangedAsync(group.Id, ct);
        }
        return Result.Success(await MapAsync(group, ct));
    }

    /// <summary>The run's definition with Product Input, Apply mock-up and Generate Video set for one design.</summary>
    public static WorkflowDefinition ForTarget(WorkflowDefinition definition, Guid productId, string artworkGroupKey, GenerateVideoConfig config) =>
        definition with
        {
            Nodes = definition.Nodes.Select(node => node.Type switch
            {
                "product-input" => node with { Config = With(node.Config, ("productId", JsonValue.Create(productId.ToString("D")))) },
                "apply-mockup" => node with { Config = With(node.Config, ("mockupIds", new JsonArray()), ("artworkGroupKey", JsonValue.Create(artworkGroupKey))) },
                "generate-video" => node with { Config = JsonSerializer.SerializeToElement(config, WorkflowJson.Options) },
                _ => node
            }).ToArray()
        };

    private static JsonElement With(JsonElement config, params (string Name, JsonNode? Value)[] values)
    {
        var node = config.ValueKind == JsonValueKind.Object ? JsonNode.Parse(config.GetRawText())!.AsObject() : new JsonObject();
        foreach (var (name, value) in values) node[name] = value;
        return JsonSerializer.SerializeToElement(node);
    }

    // The steps before the video are done: the design and its mock-ups exist, and the seller approved them.
    private static WorkflowNodeRun ChildNode(WorkflowRun run, WorkflowNode node, Target target, DateTime now)
    {
        var done = node.Type is "product-input" or "apply-mockup" or "approval-gate" || WorkflowCapabilityRegistry.BatchSteps.Contains(node.Type);
        var output = node.Type switch
        {
            "product-input" => WorkflowJson.Write(new { productId = target.Product.Id }),
            "approval-gate" => WorkflowJson.Write(new { mockupIds = target.Approved.Select(x => x.Id), revisions = target.Approved.Select(x => x.MetadataRevision) }),
            _ => "{}"
        };
        return new()
        {
            Id = Guid.NewGuid(), WorkflowRunId = run.Id, NodeId = node.Id, NodeType = node.Type, Status = done ? "succeeded" : "pending",
            Attempt = 1, Progress = done ? 100 : 0, InputSnapshot = node.Config.GetRawText(), OutputSnapshot = output, CreatedAt = now, UpdatedAt = now
        };
    }

    private sealed record Target(string Key, Product Product, string ArtworkGroupKey, string DesignLabel,
        IReadOnlyList<MockupImage> Mockups, IReadOnlyList<MockupImage> Approved, IReadOnlyList<MockupImage> Frames);

    /// <summary>The products whose designs this group makes videos of.</summary>
    private async Task<IReadOnlyCollection<Guid>> ScopeAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        if (group.BatchJobId is Guid jobId)
            return (await jobProducts.FindAsync(x => x.BatchJobId == jobId && x.ProductId != null, ct)).Select(x => x.ProductId!.Value).ToHashSet();
        return (await products.FindAsync(x => x.BatchId == group.BatchId && x.UserId == group.UserId && x.DeletedAt == null, ct)).Select(x => x.Id).ToHashSet();
    }

    /// <summary>Every design of the group's products with at least one mock-up stored as a video source.</summary>
    private async Task<IReadOnlyList<Target>> TargetsAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var scope = (await ScopeAsync(group, ct)).ToArray();
        if (scope.Length == 0) return [];
        var owned = (await products.FindAsync(x => scope.Contains(x.Id) && x.UserId == group.UserId && x.DeletedAt == null, ct)).ToDictionary(x => x.Id);
        // A composite made again after its template changed is listed instead of the one it replaces.
        var stored = MockupSources.Current(await assets.FindAsync(x => scope.Contains(x.ProductId) && x.DeletedAt == null && x.ContentHash != null, ct));
        var groupedAssets = stored.Where(x => owned.ContainsKey(x.ProductId))
            .GroupBy(x => (x.ProductId, Key: x.ArtworkGroupKey ?? x.ProductId.ToString())).ToArray();
        var designIds = groupedAssets.Select(x => Guid.TryParse(x.Key.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToArray();
        var designRows = designIds.Length == 0 ? new Dictionary<Guid, DesignImage>()
            : (await designs.FindAsync(x => designIds.Contains(x.Id), ct)).ToDictionary(x => x.Id);
        var designIndexes = designRows.ToDictionary(x => x.Key, x => x.Value.VariationIndex);
        // A product keeps one approved design (Design approval), so only its mock-ups are reviewed here: the ones made
        // from a design chosen earlier stay out. A product without an approved design shows every design not rejected.
        var withApprovedDesign = designRows.Values.Where(x => x.ApprovalStatus == "approved").Select(x => x.ProductId).ToHashSet();
        bool Hidden(Guid productId, string key) =>
            Guid.TryParse(key, out var id) && designRows.TryGetValue(id, out var design)
            && (withApprovedDesign.Contains(productId) ? design.ApprovalStatus != "approved" : design.ApprovalStatus == "rejected");

        var result = new List<Target>();
        foreach (var bucket in groupedAssets)
        {
            if (Hidden(bucket.Key.ProductId, bucket.Key.Key)) continue;
            var frames = bucket.Where(x => AiStudioFrameIdentity.Read(x.Regions) is not null).ToArray();
            var mockups = bucket.Except(frames).ToArray();
            if (mockups.Length == 0) continue;
            var approved = bucket.Where(x => x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision).ToArray();
            var product = owned[bucket.Key.ProductId];
            var label = Guid.TryParse(bucket.Key.Key, out var designId) && designIndexes.TryGetValue(designId, out var variation)
                // Design generation numbers variations from 1 (DesignGenerationService); a row from before that is 0.
                ? $"Design {Math.Max(1, variation)}"
                : bucket.Key.Key == product.Id.ToString() ? "Uploaded photos" : "Other images";
            result.Add(new(RunTargetConfigResolver.TargetKey(product.Id, bucket.Key.Key), product, bucket.Key.Key, label, mockups,
                approved.Where(x => !frames.Contains(x)).ToArray(), frames));
        }
        return result.OrderBy(x => x.Product.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Product.Id)
            .ThenBy(x => x.DesignLabel, StringComparer.Ordinal).ToArray();
    }

    private async Task<RunGroupResponse> MapAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var children = await runs.FindAsync(x => x.RunGroupId == group.Id, ct);
        var runIds = children.Select(x => x.Id).ToArray();
        var steps = runIds.Length == 0 ? [] : await nodes.FindAsync(x => runIds.Contains(x.WorkflowRunId), ct);
        var stepsByRun = steps.GroupBy(x => x.WorkflowRunId).ToDictionary(x => x.Key, x => x.ToArray());
        var targets = await TargetsAsync(group, ct);
        var waiting = group.Status == "waiting_for_targets";
        var config = waiting
            ? (await SharedConfigAsync(group, ct)).Config
            : WorkflowJson.Config<GenerateVideoConfig>(WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot).Nodes.Single(x => x.Type == "generate-video"));
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);

        var rows = new List<RunGroupTargetResponse>(targets.Count);
        foreach (var target in targets)
        {
            var child = children.FirstOrDefault(x => x.ProductId == target.Product.Id && x.ArtworkGroupKey == target.ArtworkGroupKey);
            if (!waiting && child is null) continue;
            overrides.TryGetValue(target.Key, out var picked);
            var hasOverride = picked is { Scenes.Count: > 0 };
            IReadOnlyList<RunGroupScenePreview> scenes = [];
            IReadOnlyList<string> warnings = [];
            string? resolveError = null;
            if (waiting && target.Approved.Count > 0)
            {
                var resolved = await targetConfigs.ResolveAsync(group.UserId, target.Product.Id, target.ArtworkGroupKey, ConfigFor(config, picked, target.Product.Id), picked, ct);
                var byId = target.Approved.Concat(target.Frames).ToDictionary(x => x.Id);
                scenes = resolved.Scenes.Select(scene => new RunGroupScenePreview(scene.SceneIndex, scene.MockupId,
                    scene.MockupId is Guid id && byId.TryGetValue(id, out var asset) ? Preview(asset) : null, scene.Warning, scene.Overridden)).ToArray();
                warnings = resolved.Warnings;
                resolveError = resolved.Error;
            }
            var cover = SceneSourceResolver.Hero(target.Approved) ?? SceneSourceResolver.Hero(target.Mockups);
            var current = child is null ? null : CurrentStep(stepsByRun.GetValueOrDefault(child.Id) ?? []);
            rows.Add(new(target.Key, target.Product.Id, target.Product.Name, target.Product.ProductType, target.ArtworkGroupKey,
                target.DesignLabel, cover is null ? null : Preview(cover), target.Mockups.Count, target.Approved.Count,
                target.Mockups.Count(x => x.ApprovalStatus == "pending"), waiting ? target.Approved.Count > 0 : child is not null,
                hasOverride, scenes, warnings, resolveError, child?.Id, child?.Status, current?.NodeType, current?.Stage, current?.Progress ?? 0,
                child?.ErrorMessage, child is null ? picked?.Config ?? ProductOwn(config, target.Product.Id) : RunConfig(stepsByRun.GetValueOrDefault(child.Id)),
                picked?.Config is not null));
        }
        var progress = new RunGroupProgress(children.Count, children.Count(x => x.Status == "queued"), children.Count(x => x.Status == "running"),
            children.Count(x => x.Status == "waiting_for_review"), children.Count(x => x.Status == "completed"),
            children.Count(x => x.Status == "failed"), children.Count(x => x.Status == "cancelled"), children.Count(x => x.Status == "waiting_for_input"));
        return new(group.Id, group.WorkflowId, group.BatchId, group.BatchJobId, StatusOf(group, children), group.Revision,
            group.WorkflowRevision, group.CreatedAt, MaximumTargets, rows.Count(x => x.Selected), progress, rows);
    }

    /// <summary>
    /// The scene list of a group still waiting for its designs: the workflow's saved Generate Video settings, so a
    /// change the seller saves while choosing designs is what the videos use, or the snapshot taken at the start when
    /// the saved settings could not make a video for several designs.
    /// </summary>
    private async Task<(WorkflowDefinition Definition, GenerateVideoConfig Config)> SharedConfigAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot);
        var config = WorkflowJson.Config<GenerateVideoConfig>(definition.Nodes.Single(x => x.Type == "generate-video"));
        if (await workflows.GetByIdAsync(group.WorkflowId, ct) is not { DeletedAt: null } workflow || workflow.Revision == group.WorkflowRevision)
            return (definition, config);
        try
        {
            var latest = WorkflowJson.Read<WorkflowDefinition>(workflow.Definition);
            if (graph.Validate(latest, requireProduct: false) is null &&
                latest.Nodes.SingleOrDefault(x => x.Type == "generate-video") is { } node &&
                WorkflowJson.Config<GenerateVideoConfig>(node) is var saved && WorkflowGraphValidator.SupportsBatchVideos(saved))
                return (definition, saved);
        }
        catch (JsonException) { }
        return (definition, config);
    }

    /// <summary>A running group whose runs have all finished reads as completed.</summary>
    private static string StatusOf(WorkflowRunGroup group, IReadOnlyCollection<WorkflowRun> children) =>
        group.Status == "running" && children.Count > 0 && children.All(x => FinishedRun.Contains(x.Status)) ? "completed" : group.Status;

    private static WorkflowNodeRun? CurrentStep(IReadOnlyList<WorkflowNodeRun> steps) =>
        steps.Where(x => x.Status != "succeeded")
            .OrderBy(x => Array.IndexOf(WorkflowCapabilityRegistry.RunOrder, x.NodeType) is var order && order < 0 ? int.MaxValue : order)
            .FirstOrDefault();

    private string Preview(MockupImage asset) => storage.SignRead(asset.StorageKey, "image", asset.StorageVersion);

    private static Error Missing() => Error.NotFound("RunGroupNotFound", "Run not found.");
    private static Error WorkflowMissing() => Error.NotFound("WorkflowNotFound", "Workflow not found.");
    private static Error Stale() => Error.Conflict("StaleRevision", "The workflow or run changed. Reload before continuing.");
}
