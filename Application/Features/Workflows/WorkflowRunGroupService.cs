using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowRunGroupService
{
    Task<Result<RunGroupResponse>> StartAsync(Guid workflowId, StartRunGroupRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> GetAsync(Guid groupId, CancellationToken ct);
    Task<Result<IReadOnlyList<RunGroupSummaryResponse>>> ListAsync(Guid workflowId, CancellationToken ct);
    Task<Result<RunGroupResponse>> UpdateOverrideAsync(Guid groupId, UpdateTargetOverrideRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> UpdateConfigAsync(Guid groupId, UpdateTargetConfigRequest request, CancellationToken ct);

    /// <summary>What the AI clips of every design cost together: shown to the seller, never required to start them.</summary>
    Task<Result<AiShotQuoteResponse>> QuoteAiAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> ConfirmAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);
    Task<Result<RunGroupResponse>> CancelAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);

    /// <summary>
    /// Review Video for the whole batch: once the seller approved or rejected every video, the runs waiting there go on
    /// together. An approved video is exported (and published) with its product; a rejected one is left out and its
    /// product is exported without it.
    /// </summary>
    Task<Result<RunGroupResponse>> ContinueReviewAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);

    /// <summary>
    /// Goes on with a stopped group: one stopped at Mockup Approval waits there again; the stopped runs of one that had
    /// started are queued again and carry on from the step they stopped at, keeping what they had already made.
    /// </summary>
    Task<Result<RunGroupResponse>> ResumeAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct);

    /// <summary>
    /// What a started group still needs before its videos: its products' mock-ups stored as video sources and, at the
    /// same time, the model photos of its scenes with a person; then, when Mockup Approval is automatic, the start of the
    /// videos. Run in the background as the group's owner.
    /// </summary>
    Task PrepareAsync(Guid groupId, bool autoApprove, CancellationToken ct);

    /// <summary>
    /// The last step of <see cref="PrepareAsync"/>, run in a scope of its own once the mock-ups are stored and the model
    /// photos made: in auto mode approves the mock-ups and starts the videos, otherwise keeps why the group waits.
    /// </summary>
    Task FinishPreparingAsync(Guid groupId, bool autoApprove, IReadOnlyList<string> problems, CancellationToken ct);
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
    ILifestyleMockupService? lifestyle = null, IRepository<MockupTemplate>? mockupTemplates = null,
    IRunGroupStartQueue? startQueue = null, IRepository<PromoVideo>? videos = null,
    IContentModerationService? moderation = null, IGeneratedMockupSourceService? generatedMockups = null,
    IOwnerScopes? ownerScopes = null) : IWorkflowRunGroupService
{
    /// <summary>Videos one group makes at most.</summary>
    public const int MaximumTargets = 10;

    /// <summary>Products whose mock-ups are stored at the same time (each stores up to twelve at once itself).</summary>
    public const int StoreConcurrency = 3;

    /// <summary>
    /// Model photos made at the same time. Two groups prepared at once ask Ideogram for eight, within the ten one key
    /// may have in flight.
    /// </summary>
    public const int ModelPhotoConcurrency = 4;

    private static readonly string[] FinishedRun = ["completed", "failed", "cancelled"];

    public async Task<Result<RunGroupResponse>> StartAsync(Guid workflowId, StartRunGroupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            return Result.Failure<RunGroupResponse>(Error.Validation("Provide an idempotency key of 1–100 characters."));
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
        // Without a video, each design approved at Mockup Approval goes on to the steps after it (listing, publishing).
        var config = VideoConfigOf(definition);
        if (config is null && !WorkflowGraphValidator.HasApprovalPipeline(definition))
            return Result.Failure<RunGroupResponse>(Error.Validation("Add Apply Mockup and Mockup Approval to run the workflow for each design."));
        if (config is not null && !WorkflowGraphValidator.SupportsBatchVideos(config))
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
        if (config is not null)
        {
            var videoCheck = await VideoPlanChecks.CheckAsync(entitlements, userId, config, 1, ct);
            if (videoCheck.IsFailure) return Result.Failure<RunGroupResponse>(videoCheck.Error);
        }

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
        var auto = MockupAutoApproval.IsAuto(definition);
        // The seller saw what the Run costs, model photos and AI clips included, before pressing Run: nothing paid after
        // it waits for a second confirmation. Storing the mock-ups takes Cloudinary seconds for each, a model photo takes
        // seconds at Ideogram and starting the videos plans each design's storyboard, so all of it is done in the
        // background and Run answers at once; the mock-ups appear at Mockup Approval as they are stored.
        var needsPhotos = config is not null && NeedsModelPhotos(config);
        if (startQueue is not null)
        {
            startQueue.Enqueue(new(group.Id, userId, auto));
            return Result.Success(await MapAsync(group, ct));
        }
        await StoreMockupsAsync(group, ct);
        var photoProblems = needsPhotos ? (await MakeModelPhotosAsync(group, ct)).Problems : [];
        if (auto) return Result.Success(await AutoConfirmAsync(group, photoProblems, ct));
        if (photoProblems.Count > 0) await SaveNoticeAsync(group, string.Join(" ", photoProblems), ct);
        return Result.Success(await MapAsync(group, ct));
    }

    private static bool NeedsModelPhotos(GenerateVideoConfig config) =>
        PersonModelPhotos.Needs(config) || config.ProductConfigs?.Values.Any(PersonModelPhotos.Needs) == true;

    /// <summary>The Generate Video settings of a definition, or null for a workflow without a video.</summary>
    private static GenerateVideoConfig? VideoConfigOf(WorkflowDefinition definition) =>
        definition.Nodes.SingleOrDefault(x => x.Type == "generate-video") is { } node ? WorkflowJson.Config<GenerateVideoConfig>(node) : null;

    private static bool HasVideo(WorkflowRunGroup group) =>
        WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot).Nodes.Any(x => x.Type == "generate-video");

    private static Error NoVideo() => Error.Validation("This run makes no video, so its designs have no video settings.");

    public async Task PrepareAsync(Guid groupId, bool autoApprove, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.Status != "waiting_for_targets") return;
        try
        {
            if (ownerScopes is null)
            {
                await StoreMockupsAsync(group, ct);
                await FinishPreparingAsync(group, autoApprove, (await MakeModelPhotosAsync(group, ct)).Problems, ct);
            }
            else
            {
                var problems = await PrepareSideBySideAsync(group, ownerScopes, ct);
                // Finished in a fresh scope: this one still holds the mock-ups as they were before they were stored.
                await ownerScopes.RunAsync(group.UserId, async services =>
                {
                    await services.GetRequiredService<IWorkflowRunGroupService>().FinishPreparingAsync(groupId, autoApprove, problems, ct);
                    return true;
                });
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await SaveNoticeAsync(group, "The videos could not be made ready by themselves. Choose Continue to start them.", ct);
            throw;
        }
        finally
        {
            // Told last: the seller's screen reads the group again and must find it no longer being prepared.
            startQueue?.Done(groupId);
            if (notifier != null) await notifier.GroupChangedAsync(groupId, ct);
        }
    }

    public async Task FinishPreparingAsync(Guid groupId, bool autoApprove, IReadOnlyList<string> problems, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return;
        await FinishPreparingAsync(group, autoApprove, problems, ct);
    }

    private async Task FinishPreparingAsync(WorkflowRunGroup group, bool autoApprove, IReadOnlyList<string> problems, CancellationToken ct)
    {
        // A run the seller cancelled meanwhile has nothing left to start.
        if (group.Status != "waiting_for_targets") return;
        if (autoApprove) await AutoConfirmAsync(group, problems, ct);
        else if (problems.Count > 0) await SaveNoticeAsync(group, string.Join(" ", problems), ct);
    }

    /// <summary>
    /// Stores the mock-ups and makes the model photos at the same time: both wait on other services (Cloudinary,
    /// Ideogram), so each product's mock-ups and each design's photo get a scope of their own and run side by side. A
    /// photo is made from its design's composite link, so it does not wait for the mock-ups to be stored. The seller's
    /// screen is told as each product's mock-ups are stored and as each photo is made. Returns why photos could not be made.
    /// </summary>
    private async Task<IReadOnlyList<string>> PrepareSideBySideAsync(WorkflowRunGroup group, IOwnerScopes scopes, CancellationToken ct)
    {
        var plan = await PlanModelPhotosAsync(group, includeUnstored: true, ct);
        var productIds = await ScopeAsync(group, ct);

        var storing = Parallel.ForEachAsync(productIds, new ParallelOptions { MaxDegreeOfParallelism = StoreConcurrency, CancellationToken = ct },
            async (productId, token) =>
            {
                await scopes.RunAsync(group.UserId, services => services.GetRequiredService<IGeneratedMockupSourceService>().ImportAsync(productId, token));
                if (notifier != null) await notifier.GroupChangedAsync(group.Id, token);
            });
        var failed = new string?[plan.Photos.Count];
        var dressing = Parallel.ForEachAsync(Enumerable.Range(0, plan.Photos.Count),
            new ParallelOptions { MaxDegreeOfParallelism = ModelPhotoConcurrency, CancellationToken = ct },
            async (index, token) =>
            {
                var photo = plan.Photos[index];
                var generated = await scopes.RunAsync(group.UserId, services => services.GetRequiredService<ILifestyleMockupService>()
                    .GenerateForVideoAsync(photo.ProductId, photo.GarmentId, photo.ModelId, token));
                if (generated.IsFailure) failed[index] = PhotoProblem(photo, generated.Error);
                else if (notifier != null) await notifier.GroupChangedAsync(group.Id, token);
            });
        await Task.WhenAll(storing, dressing);
        return [.. plan.Problems, .. failed.OfType<string>()];
    }

    /// <summary>Stores the composited mock-ups of the group's products as video sources, one product after another.</summary>
    private async Task StoreMockupsAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        if (generatedMockups is null) return;
        foreach (var productId in await ScopeAsync(group, ct))
            await generatedMockups.ImportAsync(productId, ct);
    }

    /// <summary>
    /// Mockup Approval in auto mode: approves every design's mock-ups waiting for review (the model photos just made
    /// among them) and starts the videos at once, AI clips included: their cost was shown when the seller pressed Run.
    /// When they cannot start (too many designs, no video left on the plan...), the group waits at Mockup Approval as in
    /// manual mode, with the mock-ups already approved, and says why (<see cref="RunGroupResponse.Notice"/>), starting
    /// with the model photos that could not be made. A design that cannot be made (no model photo for its scene with a
    /// person...) is left out, with the reason, instead of holding up the others.
    /// </summary>
    private async Task<RunGroupResponse> AutoConfirmAsync(WorkflowRunGroup group, IReadOnlyList<string> photoProblems, CancellationToken ct)
    {
        await ApproveWaitingAsync(group, ct);
        var confirmed = await ConfirmGroupAsync(group.Id, new(group.Revision), leaveOutProblems: true, ct);
        if (confirmed.IsSuccess) return confirmed.Value;
        return await MapAsync(await SaveNoticeAsync(group, string.Join(" ", photoProblems.Append(confirmed.Error.Message)), ct), ct);
    }

    private const int NoticeLength = 1000;

    private static string Clip(string notice) => notice.Length <= NoticeLength ? notice : notice[..(NoticeLength - 1)] + "…";

    /// <summary>Keeps why the group waits, for the seller to read: the group's message column.</summary>
    private async Task<WorkflowRunGroup> SaveNoticeAsync(WorkflowRunGroup group, string notice, CancellationToken ct)
    {
        var current = await groups.GetByIdAsync(group.Id, ct) ?? group;
        // A run the seller cancelled meanwhile has nothing left to wait for.
        if (current.Status != "waiting_for_targets") return current;
        current.ErrorMessage = Clip(notice);
        current.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await uow.SaveChangesAsync(ct);
        if (notifier != null) await notifier.GroupChangedAsync(current.Id, ct);
        return current;
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
    /// alone. Returns how many were made (they wait for review) and, for each design whose photo could not be made (no
    /// system model, no mock-up, Ideogram failing), why: the next Continue tries those again.
    /// </summary>
    private async Task<(int Made, IReadOnlyList<string> Problems)> MakeModelPhotosAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var plan = await PlanModelPhotosAsync(group, includeUnstored: false, ct);
        var made = 0;
        var problems = plan.Problems.ToList();
        foreach (var photo in plan.Photos)
        {
            var generated = await lifestyle!.GenerateForVideoAsync(photo.ProductId, photo.GarmentId, photo.ModelId, ct);
            if (generated.IsSuccess)
            {
                made++;
                // Told as each photo is made, not once at the end: the design's card swaps its placeholder for the photo
                // and the seller can review it while the next design's photo is still being made.
                if (notifier != null) await notifier.GroupChangedAsync(group.Id, ct);
            }
            else problems.Add(PhotoProblem(photo, generated.Error));
        }
        return (made, problems);
    }

    /// <summary>A model photo to make: the design's mock-up the system's model is dressed in.</summary>
    private sealed record PlannedPhoto(Guid ProductId, Guid GarmentId, Guid ModelId, string DesignName);

    private sealed record ModelPhotoPlan(IReadOnlyList<PlannedPhoto> Photos, IReadOnlyList<string> Problems)
    {
        public static readonly ModelPhotoPlan None = new([], []);
    }

    private static string PhotoProblem(PlannedPhoto photo, Error error) => $"{photo.DesignName}: its model photo could not be made. {error.Message}";

    /// <summary>
    /// The model photos the group's designs still need (scenes with a person, no photo yet), and why the others cannot
    /// have one. <paramref name="includeUnstored"/> also takes a design whose mock-ups are composited but not stored yet:
    /// its photo is made from the composite link, while the mock-ups are being stored.
    /// </summary>
    private async Task<ModelPhotoPlan> PlanModelPhotosAsync(WorkflowRunGroup group, bool includeUnstored, CancellationToken ct)
    {
        if (lifestyle is null || mockupTemplates is null) return ModelPhotoPlan.None;
        if ((await SharedConfigAsync(group, ct)).Config is not { } shared) return ModelPhotoPlan.None;
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        var needing = (await TargetsAsync(group, includeUnstored, ct))
            .Where(target => PersonModelPhotos.Needs(ConfigFor(shared, overrides.GetValueOrDefault(target.Key), target.Product.Id)) && !PersonModelPhotos.Has(target.Mockups))
            .ToArray();
        if (needing.Length == 0) return ModelPhotoPlan.None;

        var models = await mockupTemplates.FindAsync(x => x.UserId == null && x.Kind == MockupRules.ModelKind && x.IsActive == true, ct);
        var photos = new List<PlannedPhoto>();
        var problems = new List<string>();
        foreach (var target in needing)
        {
            if (PersonModelPhotos.DefaultModel(models, target.Product.ProductType) is not { } model)
            {
                problems.Add($"{Label(target)}: there is no system model photo for {target.Product.ProductType} to put the design on.");
                continue;
            }
            // A stored mock-up when the design has one: storing drops a composite that is already stored, so its link is
            // only used while nothing of the design is stored yet.
            var wearable = target.Mockups.Any(x => x.ContentHash != null) ? target.Mockups.Where(x => x.ContentHash != null) : target.Mockups;
            // One paid attempt per start, from the main mock-up: a failure is tried again on the next Continue, not here.
            var garments = PersonModelPhotos.Garments(wearable);
            if (garments.Count == 0)
            {
                problems.Add($"{Label(target)}: it has no mock-up to put on a model photo.");
                continue;
            }
            photos.Add(new(target.Product.Id, garments[0].Id, model.Id, Label(target)));
        }
        return new(photos, problems);
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
        if (!HasVideo(group)) return Result.Failure<RunGroupResponse>(NoVideo());
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
            // Checked once here, before the group is locked (the check may wait on Gemini): its runs plan from it unchecked.
            if (await moderation.CheckSellerTextsAsync(config, user.UserId!.Value, ModerationSubjects.AiShot, groupId, ct) is { } blocked)
                return Result.Failure<RunGroupResponse>(blocked);
        }
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<RunGroupResponse>(Error.Conflict("TargetsConfirmed", "The videos have started: change a design's video from its own run."));
        if (!HasVideo(group)) return Result.Failure<RunGroupResponse>(NoVideo());
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
    private static GenerateVideoConfig? ProductOwn(GenerateVideoConfig? shared, Guid productId) =>
        shared?.ProductConfigs?.ContainsKey(productId) == true ? shared.ForProduct(productId) : null;

    /// <summary>
    /// One design that gets a run: its mock-ups, the video settings resolved for it (null without a video), and the
    /// run it will get.
    /// </summary>
    private sealed record TargetPlan(Target Target, GenerateVideoConfig? Config, WorkflowDefinition Child, WorkflowRun Run);

    /// <summary>
    /// The videos a run group would make now. The AI quote plans these runs before they exist and Confirm stores
    /// them, so the cost the seller confirms belongs to exactly the designs and settings that then render.
    /// </summary>
    /// <param name="leftOut">
    /// When given, a design that cannot be made is left out and its reason added here, so the others still get their
    /// video; it only fails when none can be made. When null, one such design fails the whole run.
    /// </param>
    private async Task<Result<IReadOnlyList<TargetPlan>>> PlanTargetsAsync(WorkflowRunGroup group, WorkflowDefinition definition,
        GenerateVideoConfig? shared, DateTime now, CancellationToken ct, List<string>? leftOut = null)
    {
        var targets = (await TargetsAsync(group, ct)).Where(x => x.Approved.Count > 0).ToArray();
        if (targets.Length == 0)
            return Result.Failure<IReadOnlyList<TargetPlan>>(Error.Conflict("WaitingForInput", shared is null
                ? "Approve at least one mock-up of the designs to continue with."
                : "Approve at least one mock-up of the designs that should get a video."));
        if (targets.Length > MaximumTargets)
            return Result.Failure<IReadOnlyList<TargetPlan>>(Error.Validation(shared is null
                ? $"{targets.Length} designs have approved mock-ups; one run continues with at most {MaximumTargets}. Reject the mock-ups of the designs to leave out."
                : $"{targets.Length} designs have approved mock-ups; one run makes at most {MaximumTargets} videos. Reject the mock-ups of the designs to leave out."));

        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        var plans = new List<TargetPlan>(targets.Length);
        var problems = new List<string>();
        foreach (var target in targets)
        {
            if (shared is null)
            {
                var plain = ForTarget(definition, target.Product.Id, target.ArtworkGroupKey, null);
                plans.Add(new(target, null, plain, ChildRun(group, plain, target, now, plans.Count)));
                continue;
            }
            overrides.TryGetValue(target.Key, out var picked);
            var resolved = await targetConfigs.ResolveAsync(group.UserId, target.Product.Id, target.ArtworkGroupKey, ConfigFor(shared, picked, target.Product.Id), picked, ct);
            if (resolved.Error != null) { problems.Add($"{Label(target)}: {resolved.Error}"); continue; }
            var child = ForTarget(definition, target.Product.Id, target.ArtworkGroupKey, resolved.Config);
            plans.Add(new(target, resolved.Config, child, ChildRun(group, child, target, now, plans.Count)));
        }
        if (leftOut is not null)
        {
            leftOut.AddRange(problems);
            return plans.Count > 0
                ? Result.Success<IReadOnlyList<TargetPlan>>(plans)
                : Result.Failure<IReadOnlyList<TargetPlan>>(Error.Validation(string.Join(" ", problems)));
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

    /// <summary>The videos of a group that makes them; every plan of such a group has its video settings.</summary>
    private static IReadOnlyList<PlannedVideo> ForQuote(IReadOnlyList<TargetPlan> plans) => plans
        .Select(plan => new PlannedVideo(plan.Run, plan.Config!, Label(plan.Target), SourceFingerprint(plan.Target))).ToArray();

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
        return await QuoteGroupAsync(group, null, ct);
    }

    private async Task<Result<AiShotQuoteResponse>> QuoteGroupAsync(WorkflowRunGroup group, List<string>? leftOut, CancellationToken ct)
    {
        var (definition, config) = await SharedConfigAsync(group, ct);
        if (config is null) return Result.Failure<AiShotQuoteResponse>(NoVideo());
        var plans = await PlanTargetsAsync(group, definition, config, time.GetUtcNow().UtcDateTime, ct, leftOut);
        return plans.IsFailure
            ? Result.Failure<AiShotQuoteResponse>(plans.Error)
            : await quotes!.QuoteBatchAsync(group.Id, ForQuote(plans.Value), ct);
    }

    public async Task<Result<RunGroupResponse>> ConfirmAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        // A model photo that could not be made when the run started is tried again here. With Mockup Approval on auto it
        // is approved at once, so Continue goes on; in manual mode it waits for the seller's review like the others.
        // Not while the background worker is still making them: Continue is open to the designs whose photos are done,
        // and a second attempt at the others would pay the image provider twice for the same photo.
        if (await groups.GetByIdAsync(groupId, ct) is { Status: "waiting_for_targets" } waiting && waiting.UserId == user.UserId
            && waiting.Revision == request.ExpectedRevision && startQueue?.IsPending(groupId) != true)
        {
            var (made, _) = await MakeModelPhotosAsync(waiting, ct);
            if (made > 0 && MockupAutoApproval.IsAuto(WorkflowJson.Read<WorkflowDefinition>(waiting.DefinitionSnapshot)))
                await ApproveWaitingAsync(waiting, ct);
        }
        return await ConfirmGroupAsync(groupId, request, leaveOutProblems: false, ct);
    }

    /// <param name="leaveOutProblems">Makes the videos that can be made and leaves the other designs out (auto mode).</param>
    private async Task<Result<RunGroupResponse>> ConfirmGroupAsync(Guid groupId, RunGroupRevisionRequest request, bool leaveOutProblems, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "waiting_for_targets")
            return Result.Failure<RunGroupResponse>(Error.Conflict("TargetsConfirmed", "The videos of this run have already started."));

        var (definition, config) = await SharedConfigAsync(group, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var leftOut = leaveOutProblems ? new List<string>() : null;
        var planned = await PlanTargetsAsync(group, definition, config, now, ct, leftOut);
        if (planned.IsFailure) return Result.Failure<RunGroupResponse>(planned.Error);
        var plans = planned.Value;

        var fullRun = await entitlements.RequireAllowanceAsync(group.UserId, UsageAllowances.FullRuns, cancellationToken: ct);
        if (fullRun.IsFailure) return Result.Failure<RunGroupResponse>(fullRun.Error);
        if (config is not null)
        {
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
        }

        foreach (var plan in plans)
        {
            await runs.AddAsync(plan.Run, cancellationToken: ct);
            foreach (var node in plan.Child.Nodes)
                await nodes.AddAsync(ChildNode(plan.Run, node, plan.Target, now), cancellationToken: ct);
        }
        // The group keeps the scene list its videos were made with.
        if (config is not null)
            group.DefinitionSnapshot = WorkflowJson.Write(definition with
            {
                Nodes = definition.Nodes.Select(node => node.Type == "generate-video"
                    ? node with { Config = JsonSerializer.SerializeToElement(config, WorkflowJson.Options) } : node).ToArray()
            });
        group.Status = "running"; group.Revision++; group.UpdatedAt = now;
        group.ErrorMessage = leftOut is { Count: > 0 } ? Clip("Left out of the run: " + string.Join(" ", leftOut)) : null;
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
        {
            foreach (var job in await mediaJobs.FindAsync(x => x.WorkflowRunId == run.Id && (x.Status == "queued" || x.Status == "leased" || x.Status == AiClipPhase.JobStatus || x.Status == DeferredRender.JobStatus), ct))
            { job.Status = "cancelled"; job.LeaseToken = null; }
            if (videos is not null)
                await WorkflowRunStops.EndUnfinishedVideosAsync(videos, run.Id, ct,
                    (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video" && x.Status == "succeeded", ct)).Any());
        }
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

    public async Task<Result<RunGroupResponse>> ContinueReviewAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        if (videos is null) return Result.Failure<RunGroupResponse>(Error.Validation("Video review is not available."));
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "running") return Result.Failure<RunGroupResponse>(NothingToReview());

        var children = (await runs.FindAsync(x => x.RunGroupId == group.Id, ct)).OrderBy(x => x.Id).ToArray();
        var childIds = children.Select(x => x.Id).ToArray();
        // Every video is decided on together: one still being made would otherwise be exported alone later.
        var rendered = (await nodes.FindAsync(x => childIds.Contains(x.WorkflowRunId) && x.NodeType == "generate-video" && x.Status == "succeeded", ct))
            .Select(x => x.WorkflowRunId).ToHashSet();
        if (children.Any(x => !FinishedRun.Contains(x.Status) && !rendered.Contains(x.Id)))
            return Result.Failure<RunGroupResponse>(Error.Conflict("VideosNotReady", "Wait until every video has been made, then continue."));

        var decided = new List<(WorkflowRun Run, WorkflowNodeRun Review)>();
        var undecided = 0;
        // Locked in a fixed order, as each run is locked when it is approved or rejected.
        foreach (var child in children.Where(x => x.Status == "waiting_for_review"))
        {
            var run = await state.LockRunAsync(child.Id, ct);
            if (run is not { Status: "waiting_for_review" }) return Result.Failure<RunGroupResponse>(Stale());
            var review = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "review-video", ct)).Single();
            var video = await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(review.OutputSnapshot).VideoId, ct);
            if (video is { ApprovalStatus: "approved" or "rejected" } && DeferredRender.CanBeReviewed(video)) decided.Add((run, review));
            else undecided++;
        }
        if (decided.Count == 0 && undecided == 0) return Result.Failure<RunGroupResponse>(NothingToReview());
        if (undecided > 0)
            return Result.Failure<RunGroupResponse>(Error.Conflict("WaitingForReview",
                $"Approve or reject every video first: {undecided} still {(undecided == 1 ? "waits" : "wait")} for your decision."));

        // Queued again, the planner carries each run on from Review Video: its export waits until every video of the
        // batch has passed review, then packages the product with its video when approved and without it when rejected.
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (run, review) in decided)
        {
            review.Status = "succeeded"; review.UpdatedAt = now;
            run.Status = "queued"; run.Revision++; run.UpdatedAt = now;
        }
        group.Revision++; group.UpdatedAt = now;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        planner?.Signal();
        if (notifier != null)
        {
            foreach (var (run, _) in decided) await notifier.RunChangedAsync(run, ct);
            await notifier.GroupChangedAsync(group.Id, ct);
        }
        return Result.Success(await MapAsync(group, ct));
    }

    private static Error NothingToReview() => Error.Conflict("InvalidCheckpoint", "No video of this run is waiting for review.");

    public async Task<Result<RunGroupResponse>> ResumeAsync(Guid groupId, RunGroupRevisionRequest request, CancellationToken ct)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);
        var group = await state.LockRunGroupAsync(groupId, ct);
        if (group is null || group.UserId != user.UserId) return Result.Failure<RunGroupResponse>(Missing());
        if (group.Revision != request.ExpectedRevision) return Result.Failure<RunGroupResponse>(Stale());
        if (group.Status != "cancelled")
            return Result.Failure<RunGroupResponse>(Error.Conflict("InvalidCheckpoint", "Only a stopped run can be resumed."));
        var now = time.GetUtcNow().UtcDateTime;
        var children = await runs.FindAsync(x => x.RunGroupId == group.Id, ct);
        var stopped = children.Where(x => x.Status == "cancelled").ToArray();
        // A stopped run is not counted against the plan (see UsageAllowanceService), so going on with it counts again:
        // as one full run unless a run of the group still counts, and one video per design that has not finished its own.
        var counted = children.Any(x => x.Status is not ("cancelled" or "failed"));
        if (!counted)
        {
            var fullRun = await entitlements.RequireAllowanceAsync(group.UserId, UsageAllowances.FullRuns, cancellationToken: ct);
            if (fullRun.IsFailure) return Result.Failure<RunGroupResponse>(fullRun.Error);
        }
        var config = VideoConfigOf(WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot));
        var toRender = new List<WorkflowRun>();
        foreach (var child in stopped)
            if ((await nodes.FindAsync(x => x.WorkflowRunId == child.Id && x.NodeType == "generate-video" && x.Status != "succeeded", ct)).Count > 0)
                toRender.Add(child);
        if (config is not null && toRender.Count > 0)
        {
            var videoCheck = await VideoPlanChecks.CheckAsync(entitlements, group.UserId, config, toRender.Count, ct);
            if (videoCheck.IsFailure) return Result.Failure<RunGroupResponse>(videoCheck.Error);
        }

        var resumed = new List<WorkflowRun>();
        foreach (var child in stopped)
        {
            var run = await state.LockRunAsync(child.Id, ct);
            if (run is not { Status: "cancelled" }) continue;
            // Queued again, the planner carries the run on from the step it stopped at; what it made stays.
            WorkflowRunStops.Reopen(run, await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct), now);
            run.Status = "queued"; run.Revision++;
            resumed.Add(run);
        }
        // Stopped at Mockup Approval, the group waits there again.
        group.Status = children.Count == 0 ? "waiting_for_targets" : "running";
        group.CompletedAt = null; group.ErrorMessage = null; group.Revision++; group.UpdatedAt = now;
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (resumed.Count > 0) planner?.Signal();
        if (notifier != null)
        {
            foreach (var run in resumed) await notifier.RunChangedAsync(run, ct);
            await notifier.GroupChangedAsync(group.Id, ct);
        }
        return Result.Success(await MapAsync(group, ct));
    }

    /// <summary>The run's definition with Product Input, Apply mock-up and Generate Video (if any) set for one design.</summary>
    public static WorkflowDefinition ForTarget(WorkflowDefinition definition, Guid productId, string artworkGroupKey, GenerateVideoConfig? config) =>
        definition with
        {
            Nodes = definition.Nodes.Select(node => node.Type switch
            {
                "product-input" => node with { Config = With(node.Config, ("productId", JsonValue.Create(productId.ToString("D")))) },
                "apply-mockup" => node with { Config = With(node.Config, ("mockupIds", new JsonArray()), ("artworkGroupKey", JsonValue.Create(artworkGroupKey))) },
                "generate-video" when config is not null => node with { Config = JsonSerializer.SerializeToElement(config, WorkflowJson.Options) },
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
    private Task<IReadOnlyList<Target>> TargetsAsync(WorkflowRunGroup group, CancellationToken ct) => TargetsAsync(group, includeUnstored: false, ct);

    /// <param name="includeUnstored">Also counts the composited mock-ups not stored yet (only their link exists).</param>
    private async Task<IReadOnlyList<Target>> TargetsAsync(WorkflowRunGroup group, bool includeUnstored, CancellationToken ct)
    {
        var scope = (await ScopeAsync(group, ct)).ToArray();
        if (scope.Length == 0) return [];
        var owned = (await products.FindAsync(x => scope.Contains(x.Id) && x.UserId == group.UserId && x.DeletedAt == null, ct)).ToDictionary(x => x.Id);
        // A composite made again after its template changed is listed instead of the one it replaces.
        var stored = MockupSources.Current(await assets.FindAsync(x => scope.Contains(x.ProductId) && x.DeletedAt == null
            && (x.ContentHash != null || (includeUnstored && x.SourceType == MockupSources.Generated && x.MockupImageUrl != null)), ct));
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
            : VideoConfigOf(WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot));
        var overrides = RunTargetConfigResolver.ReadOverrides(group.TargetOverrides);
        var preparing = waiting && group.ErrorMessage is null && startQueue?.IsPending(group.Id) == true;

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
            if (waiting && target.Approved.Count > 0 && config is not null)
            {
                var resolved = await targetConfigs.ResolveAsync(group.UserId, target.Product.Id, target.ArtworkGroupKey, ConfigFor(config, picked, target.Product.Id), picked, ct);
                var byId = target.Approved.Concat(target.Frames).ToDictionary(x => x.Id);
                scenes = resolved.Scenes.Select(scene => new RunGroupScenePreview(scene.SceneIndex, scene.MockupId,
                    scene.MockupId is Guid id && byId.TryGetValue(id, out var asset) ? Preview(asset) : null, scene.Warning, scene.Overridden)).ToArray();
                warnings = resolved.Warnings;
                resolveError = resolved.Error;
            }
            var cover = SceneSourceResolver.Hero(target.Approved) ?? SceneSourceResolver.Hero(target.Mockups);
            var childSteps = child is null ? [] : stepsByRun.GetValueOrDefault(child.Id) ?? [];
            var current = child is null ? null : CurrentStep(childSteps);
            var printifyStep = childSteps.FirstOrDefault(x => x.NodeType == WorkflowCapabilityRegistry.PrintifyStep);
            // Only a design whose scenes show a person has a model photo to wait for; the others never show a placeholder.
            var modelPhoto = waiting && config is not null && PersonModelPhotos.Needs(ConfigFor(config, picked, target.Product.Id))
                ? PersonModelPhotos.Has(target.Mockups) ? ModelPhotoStates.Ready : preparing ? ModelPhotoStates.Pending : ModelPhotoStates.Missing
                : null;
            rows.Add(new(target.Key, target.Product.Id, target.Product.Name, target.Product.ProductType, target.ArtworkGroupKey,
                target.DesignLabel, cover is null ? null : Preview(cover), target.Mockups.Count, target.Approved.Count,
                target.Mockups.Count(x => x.ApprovalStatus == "pending"), waiting ? target.Approved.Count > 0 : child is not null,
                hasOverride, scenes, warnings, resolveError, child?.Id, child?.Status, current?.NodeType, current?.Stage, current?.Progress ?? 0,
                child?.ErrorMessage, child is null ? picked?.Config ?? ProductOwn(config, target.Product.Id) : RunConfig(stepsByRun.GetValueOrDefault(child.Id)),
                picked?.Config is not null, child is null ? null : VideoStatusOf(child, childSteps),
                printifyStep is null ? null : new RunGroupStepResponse(printifyStep.Status, printifyStep.Stage, printifyStep.Progress, printifyStep.ErrorMessage,
                    PublishNodeState.WarningsIn(printifyStep.OutputSnapshot)),
                StepOf(childSteps, "generate-video"), StepOf(childSteps, "review-video"), StepOf(childSteps, "export-zip"), modelPhoto));
        }
        var progress = new RunGroupProgress(children.Count, children.Count(x => x.Status == "queued"), children.Count(x => x.Status == "running"),
            children.Count(x => x.Status == "waiting_for_review"), children.Count(x => x.Status == "completed"),
            children.Count(x => x.Status == "failed"), children.Count(x => x.Status == "cancelled"), children.Count(x => x.Status == "waiting_for_input"));
        return new(group.Id, group.WorkflowId, group.BatchId, group.BatchJobId, StatusOf(group, children), group.Revision,
            group.WorkflowRevision, group.CreatedAt, MaximumTargets, rows.Count(x => x.Selected), progress, rows, group.ErrorMessage,
            preparing);
    }

    /// <summary>
    /// The scene list of a group still waiting for its designs: the workflow's saved Generate Video settings, so a
    /// change the seller saves while choosing designs is what the videos use, or the snapshot taken at the start when
    /// the saved settings could not make a video for several designs.
    /// </summary>
    /// <remarks>A group without a video has no scene list: its config is null.</remarks>
    private async Task<(WorkflowDefinition Definition, GenerateVideoConfig? Config)> SharedConfigAsync(WorkflowRunGroup group, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(group.DefinitionSnapshot);
        if (VideoConfigOf(definition) is not { } config) return (definition, null);
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

    // The step the run is at: one of its line (the video) first, since Push to Printify goes its own way beside it.
    private static WorkflowNodeRun? CurrentStep(IReadOnlyList<WorkflowNodeRun> steps) =>
        steps.Where(x => x.Status != "succeeded")
            .OrderBy(x => x.NodeType == WorkflowCapabilityRegistry.PrintifyStep ? 1 : 0)
            .ThenBy(x => Array.IndexOf(WorkflowCapabilityRegistry.RunOrder, x.NodeType) is var order && order < 0 ? int.MaxValue : order)
            .FirstOrDefault();

    private static RunGroupStepResponse? StepOf(IReadOnlyList<WorkflowNodeRun> steps, string type) =>
        steps.FirstOrDefault(x => x.NodeType == type) is { } step
            ? new(step.Status, step.Stage, step.Progress, step.ErrorMessage) : null;

    /// <summary>
    /// The status the run would have for its video alone: its line of steps, without Push to Printify (it runs beside
    /// the video and fails on its own) or Listing Content (it never holds the video up). Null for a run without a video.
    /// </summary>
    internal static string? VideoStatusOf(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> steps)
    {
        if (steps.All(x => x.NodeType != "generate-video")) return null;
        var line = steps.Where(x => x.NodeType != WorkflowCapabilityRegistry.PrintifyStep && x.NodeType != "generate-listing").ToArray();
        if (line.Any(x => x.Status == "failed")) return "failed";
        // A video finished before the run was stopped stays made: stopping it then only stops Push to Printify.
        if (line.All(x => x.Status == "succeeded")) return "completed";
        if (run.Status == "cancelled" || line.Any(x => x.Status == "cancelled")) return "cancelled";
        if (line.Any(x => x.Status == "waiting_for_review")) return "waiting_for_review";
        if (line.Any(x => x.Status == "waiting_for_input")) return "waiting_for_input";
        return run.Status == "queued" ? "queued" : "running";
    }

    private string Preview(MockupImage asset) => storage.SignRead(asset.StorageKey, "image", asset.StorageVersion);

    private static Error Missing() => Error.NotFound("RunGroupNotFound", "Run not found.");
    private static Error WorkflowMissing() => Error.NotFound("WorkflowNotFound", "Workflow not found.");
    private static Error Stale() => Error.Conflict("StaleRevision", "The workflow or run changed. Reload before continuing.");
}
