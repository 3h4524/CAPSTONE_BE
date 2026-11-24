using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Application.Features.ListingStreams;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowRuntime(IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<VideoTemplate> templates, IRepository<MediaJob> jobs, IRepository<ExportPackage> exports,
    IRepository<ExportPackageItem> exportItems, VideoStoryboardPlanner planner, IListingContentService listingContent, TimeProvider time,
    IRepository<WorkflowRun> runs,
    IVideoExperienceContextService? experiences = null, IVideoPromptPreparationService? promptPreparation = null,
    IRepository<DesignImage>? designImages = null,
    Abstractions.BackgroundJobs.IWorkflowPublishQueue? publishQueue = null,
    IRepository<MockupTemplate>? mockupTemplates = null,
    Microsoft.Extensions.Options.IOptions<AiClipPhaseSettings>? clipPhase = null,
    Microsoft.Extensions.Options.IOptions<DeferredRenderSettings>? deferredRender = null,
    Features.Entitlements.IPlanEntitlementService? entitlements = null,
    ILogger<WorkflowRuntime>? logger = null,
    Abstractions.Storage.IGarmentLocator? garmentLocator = null)
{
    /// <summary>Generate Video ends once the scenes are ready, and the final video is rendered later, when a step needs it.</summary>
    public bool RendersOnDemand => deferredRender?.Value.Enabled == true;

    /// <summary>
    /// Stage of a Generate Video step whose AI scene prompts are being written (image analysis and rewriting, retrieved
    /// lessons) outside any transaction, before its clips are sent (<see cref="BeginGroupRunAsync"/>).
    /// </summary>
    public const string PreparingPromptsStage = "Preparing AI prompts";

    /// <summary>Stage of a listing step being written outside any transaction, after the video has gone ahead.</summary>
    public const string WritingListingStage = "Writing listing";

    /// <summary>
    /// The first, short part of planning a batch's child run, in the planner's transaction with the run locked and
    /// running. What takes long is only marked here, so the run shows as working at once and nothing waits on Gemini
    /// with a row locked: the AI scene prompts (<see cref="PreparePromptsAsync"/>) and, for a run with a video, its
    /// listing (<see cref="WriteListingAsync"/>), which then comes after the clips have been sent. A run with neither
    /// advances here as it always did. A run whose earlier attempt stopped half way (the API restarted) picks up the
    /// work that was left.
    /// </summary>
    public async Task<(bool PreparesPrompts, bool WritesListing)> BeginGroupRunAsync(WorkflowRun run, CancellationToken ct)
    {
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct);
        var generate = checkpoints.SingleOrDefault(x => x.NodeType == "generate-video");
        var listing = checkpoints.SingleOrDefault(x => x.NodeType == "generate-listing");
        var now = time.GetUtcNow().UtcDateTime;
        var stillPreparing = generate is { Status: "running", Stage: PreparingPromptsStage };
        var stillWriting = listing is { Status: "running", Stage: WritingListingStage };
        if (stillPreparing || stillWriting)
        {
            // Touched, so another planner does not take it up again while this one works.
            if (stillPreparing) generate!.UpdatedAt = now;
            if (stillWriting) listing!.UpdatedAt = now;
            return (stillPreparing, stillWriting);
        }

        // The listing goes on Etsy and Printify, never in the video, so a run with a video sends its clips first.
        var writesListing = generate is not null && listing is { Status: "pending" };
        if (writesListing)
        {
            listing!.Status = "running"; listing.Stage = WritingListingStage; listing.Progress = 10;
            listing.ErrorMessage = null; listing.UpdatedAt = now;
        }
        var preparesPrompts = generate is { Status: "pending" } && NeedsPromptPreparation(generate)
            && checkpoints.Where(x => x.NodeType is "product-input" or "apply-mockup" or "approval-gate").All(x => x.Status == "succeeded");
        if (!preparesPrompts)
        {
            await AdvanceAsync(run, ct);
            return (false, writesListing);
        }
        generate!.Status = "running"; generate.Stage = PreparingPromptsStage; generate.Progress = 2;
        generate.ErrorMessage = null; generate.UpdatedAt = now;
        // Push to Printify does not wait for the video; it does wait for a listing being written (StartPrintify).
        StartPrintify(checkpoints);
        return (true, writesListing);
    }

    /// <summary>
    /// Writes the AI scene prompts of a run's video, with no transaction open: the slow part of planning, which reads
    /// and asks Gemini but changes nothing. Null when the step is no longer waiting for it.
    /// </summary>
    public async Task<Result<Storyboard>?> PreparePromptsAsync(WorkflowRun run, CancellationToken ct)
    {
        var generate = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (generate is not { Status: "running", Stage: PreparingPromptsStage }) return null;
        return await PlanAsync(run, WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot), ct);
    }

    /// <summary>
    /// Keeps the prompts written by <see cref="PreparePromptsAsync"/> on the step, where planning finds them without
    /// asking Gemini again, and goes on with the run: its clips are sent. With the run locked and running.
    /// </summary>
    public async Task FinishPromptsAsync(WorkflowRun run, Result<Storyboard> plan, CancellationToken ct)
    {
        var generate = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (generate is not { Status: "running", Stage: PreparingPromptsStage }) return;
        generate.Stage = null; generate.Progress = 0; generate.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (plan.IsFailure)
        {
            // As planning inside the run would have ended: the seller changes the scenes and goes on.
            generate.Status = "waiting_for_input"; run.Status = "waiting_for_input";
            generate.ErrorMessage = plan.Error.Message; run.ErrorMessage = plan.Error.Message;
            return;
        }
        var config = WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
        if (config.Mode == "ai_shot")
            generate.OutputSnapshot = WorkflowJson.Write(new AiShotPromptSnapshot(WorkflowJson.Hash(WorkflowJson.Write(config)), plan.Value));
        generate.Status = "pending";
        await AdvanceAsync(run, ct);
    }

    /// <summary>
    /// Writes the listing of a run whose video has gone ahead, with no transaction open (the listing service keeps
    /// what it writes itself). Null when the step is no longer waiting for it.
    /// </summary>
    public async Task<Result<ListingContentResponseDto>?> WriteListingAsync(WorkflowRun run, CancellationToken ct)
    {
        var listing = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-listing", ct)).SingleOrDefault();
        if (listing is not { Status: "running", Stage: WritingListingStage }) return null;
        return await listingContent.GetOrGenerateAsync(run.ProductId, ct);
    }

    /// <summary>
    /// Records the listing written by <see cref="WriteListingAsync"/> and lets the steps that waited for it go on
    /// (Push to Printify, Publish to Etsy, Export ZIP). With the run locked.
    /// </summary>
    public async Task FinishListingAsync(WorkflowRun run, Result<ListingContentResponseDto> result, CancellationToken ct)
    {
        var listing = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-listing", ct)).SingleOrDefault();
        if (listing is not { Status: "running", Stage: WritingListingStage }) return;
        Record(listing, result);
        if (run.Status == "running") await AdvanceAsync(run, ct);
    }

    /// <summary>Whether the step's AI scene prompts still have to be written: AI scenes with no prompts kept for these settings.</summary>
    private static bool NeedsPromptPreparation(WorkflowNodeRun generate)
    {
        var config = WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
        if (config.Mode != "ai_shot" || config.AiScenes is not { Count: > 0 } scenes || scenes.All(scene => scene.Mode == "standard"))
            return false;
        try
        {
            var snapshot = WorkflowJson.Read<AiShotPromptSnapshot>(generate.OutputSnapshot);
            return !string.Equals(snapshot?.ConfigFingerprint, WorkflowJson.Hash(WorkflowJson.Write(config)), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>A listing being written beside the video: the steps that publish or package it wait for it.</summary>
    private static bool ListingBeingWritten(IReadOnlyList<WorkflowNodeRun> checkpoints) =>
        checkpoints.Any(x => x.NodeType == "generate-listing" && x is { Status: "running", Stage: WritingListingStage });

    public async Task<Result<Storyboard>> PlanAsync(WorkflowRun run, GenerateVideoConfig config, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        if (config.Mode == "ai_shot")
        {
            var generate = (await nodes.FindAsync(item => item.WorkflowRunId == run.Id && item.NodeType == "generate-video", ct))
                .SingleOrDefault();
            if (generate is not null)
            {
                try
                {
                    var snapshot = WorkflowJson.Read<AiShotPromptSnapshot>(generate.OutputSnapshot);
                    var configFingerprint = WorkflowJson.Hash(WorkflowJson.Write(config));
                    if (string.Equals(snapshot.ConfigFingerprint, configFingerprint, StringComparison.Ordinal))
                        return Result.Success(snapshot.Storyboard);
                }
                catch (JsonException)
                {
                    // Empty, legacy, and completed-node output snapshots are not prompt snapshots.
                }
            }
        }
        var mockupConfig = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
        var approved = await ApprovedAssetsAsync(run, mockupConfig, config, ct);
        var product = await products.GetByIdAsync(run.ProductId, ct);
        var aiTemplateCodes = AiVideoPromptCompiler.Catalog().Select(item => item.Code).ToArray();
        var aiTemplates = await templates.FindAsync(x => x.IsSystemTemplate == true && x.IsActive == true && x.Code != null && aiTemplateCodes.Contains(x.Code), ct);
        if (product is null)
            return Result.Failure<Storyboard>(Error.NotFound("ProductNotFound", "Product not found."));
        var experienceMap = experiences is null
            ? null
            : await experiences.RetrieveForScenesAsync(run.UserId, product, config, ct);
        var garments = await AiOpeningCrop.LocateGarmentsAsync(garmentLocator, product, config, approved, ct);
        var planned = planner.Plan(product, approved, config, aiTemplates, experienceMap, garments);
        if (planned.IsFailure) return planned;
        var prepared = promptPreparation is null
            ? planned.Value
            : await promptPreparation.PrepareAsync(run.UserId, planned.Value, approved, ct);
        // The seller's scene text was checked when it was stored (see AiShotPromptModeration).
        return Result.Success(prepared);
    }

    /// <param name="listingStream">Where a listing written now is streamed to, when the request asked for it.</param>
    public async Task AdvanceAsync(WorkflowRun run, CancellationToken ct, ListingStreamWriter? listingStream = null)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct);
        var listing = checkpoints.SingleOrDefault(x => x.NodeType == "generate-listing");
        if (listing is { Status: "pending" }) await GenerateListingAsync(run, checkpoints, listing, listingStream, ct);
        if (IsListingOnly(checkpoints))
        {
            if (listing is not null) FinishListingOnlyRun(run, listing);
            return;
        }
        // A listing that could not be written stays failed on its own step and can be retried there:
        // the video is made from the mock-ups and does not wait for it. Without a video, Mockup Approval leads
        // straight to the publish steps below, and a video without Export ZIP goes there from Review Video.
        var hasVideo = checkpoints.Any(x => x.NodeType == "generate-video");
        foreach (var type in hasVideo ? WorkflowCapabilityRegistry.Pipeline : WorkflowCapabilityRegistry.PublishPipeline)
        {
            var node = checkpoints.SingleOrDefault(x => x.NodeType == type);
            if (node is null && WorkflowCapabilityRegistry.OptionalVideoSteps.Contains(type)) continue;
            node ??= checkpoints.Single(x => x.NodeType == type);
            if (node.Status == "succeeded")
            {
                if (type == "approval-gate") StartPrintify(checkpoints);
                continue;
            }
            // A step waiting for the final video is looked at again each time the run advances: the render finishing is what lets it go on.
            if ((node.Status == "running" && !DeferredRender.IsWaiting(node)) || node.Status is "failed" or "cancelled") return;
            node.UpdatedAt = time.GetUtcNow().UtcDateTime;
            switch (type)
            {
                case "product-input": node.OutputSnapshot = WorkflowJson.Write(new { productId = run.ProductId }); node.Status = "succeeded"; break;
                case "apply-mockup": node.Status = "succeeded"; break;
                case "approval-gate":
                    // Auto mode approves the mock-ups itself and goes on, unless the video still needs the seller.
                    if (MockupAutoApproval.IsAuto(definition) && await AutoApproveAsync(run, definition, checkpoints, node, ct))
                    {
                        StartPrintify(checkpoints);
                        break;
                    }
                    node.Status = "waiting_for_input"; run.Status = "waiting_for_input"; return;
                case "generate-video":
                    var config = WorkflowJson.Read<GenerateVideoConfig>(node.InputSnapshot);
                    var plan = await PlanAsync(run, config, ct);
                    if (plan.IsFailure)
                    {
                        node.Status = "waiting_for_input";
                        run.Status = "waiting_for_input";
                        node.ErrorMessage = plan.Error.Message;
                        run.ErrorMessage = plan.Error.Message;
                        return;
                    }
                    var board = plan.Value;
                    run.ErrorMessage = null;
                    var previous = (await videos.FindAsync(x => x.WorkflowRunId == run.Id, ct)).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
                    var series = previous?.SeriesId ?? Guid.NewGuid();
                    var reuse = previous is { Status: "failed" } && previous.Fingerprint == board.Fingerprint && node.Attempt == previous.VersionNumber;
                    var video = reuse ? previous! : new PromoVideo { Id = Guid.NewGuid(), ProductId = run.ProductId, WorkflowRunId = run.Id,
                        VideoTemplateId = (await templates.FindAsync(x => x.Code == board.Template && x.TemplateVersion == board.TemplateVersion, ct)).Single().Id,
                        Mode = board.Mode, SeriesId = series, VersionNumber = (previous?.VersionNumber ?? 0) + 1, ReviewRevision = 1,
                        Fingerprint = board.Fingerprint, TemplateVersion = board.TemplateVersion, ConfigSnapshot = WorkflowJson.Write(new { config, storyboard = board }),
                        QaResult = "{}", VideoDurationSeconds = Math.Max(1, (int)Math.Ceiling((board.OutputDurationFrames ?? config.DurationSeconds * 30) / 30d)), VideoResolution = $"{board.Width}x{board.Height}", AspectRatio = board.AspectRatio,
                        FileFormat = "mp4", PlatformTarget = "etsy", MusicTrackId = board.VideoEdit?.Music?.TrackId, Status = "queued", ApprovalStatus = "pending", IsFinal = false, CreatedAt = time.GetUtcNow().UtcDateTime };
                    if (!reuse) await videos.AddAsync(video, cancellationToken: ct);
                    video.Status = "queued";
                    foreach (var scene in reuse ? Array.Empty<ScenePlan>() : board.Scenes) await scenes.AddAsync(new() { Id = Guid.NewGuid(), PromoVideoId = video.Id, MockupImageId = scene.MockupId,
                        SceneOrder = scene.SceneOrder + 1, DurationSeconds = scene.DurationFrames / 30m, GenerationStrategy = scene.GenerationStrategy, SourceRevision = scene.SourceRevision,
                        SourceHash = scene.SourceHash, SceneConfig = WorkflowJson.Write(scene), Warnings = WorkflowJson.Write(scene.Warnings), TransitionEffect = SceneTransitionColumn.For(scene.Transition),
                        TextOverlayContent = scene.Text, CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    node.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id)); node.Status = "running"; node.Stage = "Planning"; node.Progress = 5;
                    // The video's scenes are all there: photos always are, AI clips once stored. Only then can the render wait for a step
                    // to need it; a video whose clips the media worker still has to make is rendered as before.
                    var scenesReady = true;
                    if (board.Mode == "ai_shot")
                    {
                        AiShotJobState[] retained = [];
                        int? targetSceneOrder = null;
                        var oldJob = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id && x.Kind == "render", ct)).SingleOrDefault();
                        if (oldJob is not null)
                        {
                            try
                            {
                                var previousPayload = WorkflowJson.Read<AiRenderJobPayload>(oldJob.Payload);
                                retained = previousPayload.AiShotStates?.ToArray() ?? [];
                                targetSceneOrder = previousPayload.TargetSceneOrder;
                            }
                            catch (JsonException) { /* The previous render used the Standard payload. */ }
                        }
                        // An isolated scene request keeps already stored clips for all other scene positions. This lets a
                        // seller append or replace one scene after changing the current storyboard configuration.
                        var states = (board.AiShots ?? []).Select(shot =>
                        {
                            var existing = targetSceneOrder.HasValue && shot.SceneOrder != targetSceneOrder.Value
                                ? retained.FirstOrDefault(item => item.SceneOrder == shot.SceneOrder && item.Status == "stored" &&
                                    item.StorageKey is not null && item.StorageVersion is not null)
                                : retained.FirstOrDefault(item => item.Fingerprint == shot.Fingerprint && item.Status != "failed");
                            // A scene the provider may still be making gets a fresh time limit and no memory of earlier refusals:
                            // this is a new attempt, and the API gives a scene up a set time after it was submitted.
                            return existing is not null
                                ? existing with { SceneOrder = shot.SceneOrder, Fingerprint = shot.Fingerprint,
                                    SubmittedAt = existing.Status is "polling" or "ready" ? time.GetUtcNow().UtcDateTime : existing.SubmittedAt,
                                    BusyAttempts = 0, NextAttemptAt = null }
                                : new AiShotJobState(shot.SceneOrder, shot.Fingerprint);
                        }).ToArray();
                        // With the clip phase on, the API makes the scenes that are not stored yet and the media worker gets the job
                        // once they all are, so it only renders. Without it the worker makes them itself.
                        var makeClipsHere = clipPhase?.Value.Enabled == true && states.Any(state => state.Status != "stored");
                        scenesReady = states.All(state => state.Status == "stored");
                        await EnqueueAsync(run, node, "render", new AiRenderJobPayload(video.Id, board, states), ct,
                            makeClipsHere ? AiClipPhase.JobStatus : RendersOnDemand && scenesReady ? DeferredRender.JobStatus : "queued");
                        if (makeClipsHere) { node.Stage = AiClipPhase.Stage; node.Progress = AiClipPhase.StartProgress; }
                    }
                    else await EnqueueAsync(run, node, "render", new RenderJobPayload(video.Id, board), ct,
                        RendersOnDemand ? DeferredRender.JobStatus : "queued");
                    // The scenes are all there: with the render deferred the video is ready to be shown now, and the step ends.
                    if (RendersOnDemand && scenesReady) { MarkReady(node, video); break; }
                    return;
                case "review-video":
                    node.OutputSnapshot = checkpoints.Single(x => x.NodeType == "generate-video").OutputSnapshot;
                    // Auto mode approves the finished video itself and goes on to export, unless it cannot be listed.
                    if (VideoAutoApproval.IsAuto(definition) &&
                        await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(node.OutputSnapshot).VideoId, ct) is { } candidate &&
                        VideoAutoApproval.Approve(candidate, run.UserId, time.GetUtcNow().UtcDateTime))
                    {
                        node.Status = "succeeded";
                        // If the plan refuses the render now, the step that needs the file asks again and fails with the reason.
                        if (candidate.Status == DeferredRender.VideoReady && VideoRenderDemand.Requires(checkpoints)) await RequestFinalRenderAsync(run, candidate, ct);
                        break;
                    }
                    node.Status = "waiting_for_review"; run.Status = "waiting_for_review"; return;
                case "export-zip":
                    // The package carries the listing: one still being written is waited for (FinishListingAsync goes on).
                    if (ListingBeingWritten(checkpoints)) return;
                    // A batch exports only after every video has finished and passed review. The planner
                    // resumes pending exports after the last review commits (also after a server restart).
                    if (run.RunGroupId is Guid groupId)
                    {
                        var siblings = await runs.FindAsync(x => x.RunGroupId == groupId && x.Id != run.Id, ct);
                        var siblingIds = siblings.Select(x => x.Id).ToArray();
                        var upstream = await nodes.FindAsync(x => siblingIds.Contains(x.WorkflowRunId) &&
                            (x.NodeType == "generate-video" || x.NodeType == "review-video"), ct);
                        if (siblings.Any(sibling => upstream.Count(x => x.WorkflowRunId == sibling.Id && x.Status == "succeeded") != 2))
                            return;
                    }
                    var pointer = WorkflowJson.Read<VideoPointer>(checkpoints.Single(x => x.NodeType == "review-video").OutputSnapshot);
                    var reviewedVideo = (await videos.GetByIdAsync(pointer.VideoId, ct))!;
                    // A video the seller rejected at review stays out of the package: the product is still packaged, with
                    // its mock-ups, designs, listing and guide.
                    var approvedVideo = reviewedVideo.ApprovalStatus == "approved" ? reviewedVideo : null;
                    if (approvedVideo is null && reviewedVideo.ApprovalStatus != "rejected") { node.Status = "failed"; run.Status = "failed"; run.ErrorMessage = "Export requires the exact approved video."; return; }
                    // The package needs the final video file: it is made now, in the background, if nobody asked for it earlier.
                    if (approvedVideo is not null && VideoRenderDemand.ExportNeedsFinalVideo(node) && approvedVideo.Status != DeferredRender.VideoCompleted)
                    {
                        await WaitForFinalRenderAsync(run, node, approvedVideo, ct);
                        return;
                    }
                    // Export ZIP always carries the video of every scene; the final video only when its option is on (the gate above made sure the file exists).
                    var includeFinal = approvedVideo is not null && VideoRenderDemand.ExportNeedsFinalVideo(node);
                    var videoScenes = approvedVideo is null ? []
                        : (await scenes.FindAsync(x => x.PromoVideoId == approvedVideo.Id, ct)).OrderBy(x => x.SceneOrder).ToArray();
                    var exportProduct = await products.GetByIdAsync(run.ProductId, ct);
                    var approvedRows = await assets.FindAsync(x => x.ProductId == run.ProductId && x.DeletedAt == null &&
                        x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision, ct);
                    var templateIds = approvedRows.Select(x => x.MockupTemplateId).OfType<Guid>().Distinct().ToArray();
                    var aiTemplates = mockupTemplates is null || templateIds.Length == 0 ? new HashSet<Guid>()
                        : (await mockupTemplates.FindAsync(x => templateIds.Contains(x.Id) && x.AiGenerated, ct)).Select(x => x.Id).ToHashSet();
                    // In the order to upload them to Etsy: a photo not made with AI first, then by when they were made.
                    var approvedMockups = approvedRows
                        .Select(x => (Row: x, AiMade: Shops.Common.ListingPhotos.IsAiMade(x) || x.MockupTemplateId is { } id && aiTemplates.Contains(id)))
                        .OrderBy(x => x.AiMade).ThenBy(x => x.Row.CreatedAt).ThenBy(x => x.Row.Id)
                        .Select(x => new ExportAssetSnapshot(x.Row.Id, x.Row.StorageKey, x.Row.StorageVersion, x.Row.ContentHash, x.Row.MetadataRevision, AiMade: x.AiMade))
                        .ToArray();
                    var approvedDesigns = designImages is null ? [] : (await designImages.FindAsync(x => x.ProductId == run.ProductId &&
                        x.DeletedAt == null && x.ApprovalStatus == "approved", ct))
                        .OrderBy(x => x.VariationIndex).ThenBy(x => x.Id)
                        .Select(x => new ExportAssetSnapshot(x.Id, x.StorageKey, null, null, x.VariationIndex, x.FileFormat)).ToArray();
                    ExportListingSnapshot? exportListing = null;
                    var listingNode = checkpoints.SingleOrDefault(x => x.NodeType == "generate-listing" && x.Status == "succeeded");
                    if (listingNode is not null)
                    {
                        try
                        {
                            var generatedListing = WorkflowJson.Read<ListingContentResponseDto>(listingNode.OutputSnapshot);
                            // The text as APCS would send it to Etsy: within Etsy's rules, and saying AI made the design.
                            var (exportTitle, _) = Shops.Common.EtsyListingText.Title(generatedListing.Title);
                            var (exportTags, _) = Shops.Common.EtsyListingText.Tags(generatedListing.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Value));
                            exportListing = new(exportTitle, Shops.Common.EtsyListingText.WithAiDisclosure(generatedListing.Description),
                                exportTags, generatedListing.VersionNumber);
                        }
                        catch (JsonException) { }
                    }
                    VideoExportSourceSnapshot? sceneSnapshot = null;
                    Storyboard? exportStoryboard = null;
                    if (approvedVideo is not null)
                    {
                        try
                        {
                            using var snapshotDocument = JsonDocument.Parse(approvedVideo.ConfigSnapshot ?? "{}");
                            exportStoryboard = snapshotDocument.RootElement.GetProperty("storyboard").Deserialize<Storyboard>(WorkflowJson.Options);
                            if (snapshotDocument.RootElement.TryGetProperty("exportSources", out var sourceElement))
                                sceneSnapshot = sourceElement.Deserialize<VideoExportSourceSnapshot>(WorkflowJson.Options);
                        }
                        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentNullException) { }
                        // A video that was never rendered has no record of its scene sources yet: they are read from its mock-ups and stored clips.
                        if (sceneSnapshot is null && exportStoryboard is not null)
                            sceneSnapshot = await PlannedSceneSourcesAsync(run, approvedVideo, exportStoryboard, ct);
                    }
                    object manifest = approvedVideo is null
                        ? new { schemaVersion = 1, productId = run.ProductId, videoId = (Guid?)null, scenes = Array.Empty<object>() }
                        : new { schemaVersion = 1, productId = run.ProductId, videoId = approvedVideo.Id, version = approvedVideo.VersionNumber,
                            mode = approvedVideo.Mode, fingerprint = approvedVideo.Fingerprint, approvedRevision = approvedVideo.ReviewRevision,
                            video = includeFinal ? "video.mp4" : null, thumbnail = includeFinal ? "thumbnail.png" : null, scenes = videoScenes.Select(s => new { order = s.SceneOrder, mockupId = s.MockupImageId,
                                sourceRevision = s.SourceRevision, sourceHash = s.SourceHash, generationStrategy = s.GenerationStrategy, source = $"mockups/{s.MockupImageId}.png" }) };
                    var package = new ExportPackage { Id = Guid.NewGuid(), UserId = run.UserId, WorkflowRunId = run.Id, Type = "zip_package",
                        Name = approvedVideo is null ? "Product package without video" : "Approved video package", Status = "preparing",
                        Manifest = await WithModelPhotosAsync(WorkflowJson.Write(manifest), run.ProductId, videoScenes, ct),
                        CreatedAt = time.GetUtcNow().UtcDateTime, ExpiresAt = time.GetUtcNow().AddDays(7).UtcDateTime };
                    await exports.AddAsync(package, cancellationToken: ct);
                    await exportItems.AddAsync(new() { Id = Guid.NewGuid(), ExportPackageId = package.Id, ProductId = run.ProductId, PromoVideoId = approvedVideo?.Id,
                        IncludePromoVideo = approvedVideo is not null, IncludeMockupImages = approvedMockups.Length > 0, IncludeDesignImages = approvedDesigns.Length > 0,
                        IncludeListingContent = exportListing is not null, ItemStatus = "pending", CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    var guide = EtsyGuide(run, exportProduct, approvedMockups, approvedDesigns, includeFinal ? approvedVideo : null, exportListing is not null);
                    node.OutputSnapshot = WorkflowJson.Write(new { exportId = package.Id }); node.Status = "running";
                    node.Stage = "Exporting"; node.Progress = 0;
                    await EnqueueAsync(run, node, "export_zip", new ExportJobPayload(package.Id, approvedVideo?.Id, includeFinal ? approvedVideo?.StorageKey : null, includeFinal ? approvedVideo?.StorageVersion : null,
                        includeFinal ? approvedVideo?.ThumbnailStorageKey : null, includeFinal ? approvedVideo?.ThumbnailStorageVersion : null, JsonSerializer.Deserialize<JsonElement>(package.Manifest),
                        exportProduct?.Name, exportProduct?.ProductType, approvedMockups, approvedDesigns, exportListing, exportStoryboard, sceneSnapshot, guide), ct); return;
            }
        }
        // Publishing calls Printify and Etsy for up to minutes, so it runs in the background after this transaction
        // commits; WorkflowPublishService finishes the step and advances the run again.
        foreach (var type in WorkflowCapabilityRegistry.PublishSteps.Where(type => type != WorkflowCapabilityRegistry.PrintifyStep))
        {
            var node = checkpoints.SingleOrDefault(x => x.NodeType == type);
            if (node is null || node.Status == "succeeded") continue;
            if ((node.Status == "running" && !DeferredRender.IsWaiting(node)) || node.Status is "failed" or "cancelled") return;
            // The listing is what goes on Etsy: one still being written is waited for (FinishListingAsync goes on).
            if (ListingBeingWritten(checkpoints)) return;
            // Publish to Etsy attaches the video, so it waits for the final video to be rendered, and asks for it if nobody did.
            if (type == "publish-etsy" && VideoRenderDemand.EtsyNeedsVideo(node) &&
                await ApprovedVideoWithoutFileAsync(checkpoints, ct) is { } unrendered)
            {
                await WaitForFinalRenderAsync(run, node, unrendered, ct);
                return;
            }
            StartPublish(node);
            return;
        }
        // Push to Printify goes its own way beside the video: the run is finished once it is too, and a Printify that
        // failed leaves the run failed with its reason, for the seller to retry that step alone.
        switch (checkpoints.SingleOrDefault(x => x.NodeType == WorkflowCapabilityRegistry.PrintifyStep))
        {
            case { Status: "pending" }:
                StartPrintify(checkpoints);
                return;
            case { Status: "running" or "cancelled" }:
                return;
            case { Status: "failed" } failed:
                run.Status = "failed"; run.ErrorMessage = failed.ErrorMessage;
                return;
        }
        run.Status = "completed"; run.CompletedAt = time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Push to Printify publishes the design and the approved mock-ups, never the video, so it starts as soon as Mockup
    /// Approval has passed and runs beside the video: neither waits for the other, and one failing does not stop the
    /// other. WorkflowPublishService finishes it in the background.
    /// </summary>
    private void StartPrintify(IReadOnlyList<WorkflowNodeRun> checkpoints)
    {
        // Printify takes the listing's title and description: one being written is waited for (FinishListingAsync starts it).
        if (checkpoints.SingleOrDefault(x => x.NodeType == WorkflowCapabilityRegistry.PrintifyStep) is { Status: "pending" } printify
            && checkpoints.SingleOrDefault(x => x.NodeType == "approval-gate") is { Status: "succeeded" }
            && !ListingBeingWritten(checkpoints))
            StartPublish(printify);
    }

    /// <summary>Queues a publish step for the background worker, which finishes it and advances the run again.</summary>
    private void StartPublish(WorkflowNodeRun node)
    {
        node.Status = "running"; node.Stage = "Queued"; node.Progress = 5; node.ErrorMessage = null;
        node.OutputSnapshot = WorkflowJson.Write(new PublishNodeState("queued"));
        node.UpdatedAt = time.GetUtcNow().UtcDateTime;
        publishQueue?.Enqueue(node.Id);
    }
    /// <summary>Writes the listing again after it failed, without touching the video steps.</summary>
    public async Task RetryListingAsync(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> checkpoints, CancellationToken ct,
        ListingStreamWriter? listingStream = null)
    {
        var listing = checkpoints.Single(x => x.NodeType == "generate-listing");
        listing.Attempt++;
        await GenerateListingAsync(run, checkpoints, listing, listingStream, ct);
        if (IsListingOnly(checkpoints)) FinishListingOnlyRun(run, listing);
    }

    /// <summary>A run that only writes a listing: no video and nothing to publish.</summary>
    private static bool IsListingOnly(IReadOnlyList<WorkflowNodeRun> checkpoints) =>
        checkpoints.All(x => x.NodeType != "generate-video" && !WorkflowCapabilityRegistry.PublishSteps.Contains(x.NodeType));

    private async Task GenerateListingAsync(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> checkpoints, WorkflowNodeRun listing,
        ListingStreamWriter? listingStream, CancellationToken ct)
    {
        var input = checkpoints.Single(x => x.NodeType == "product-input");
        if (input.Status != "succeeded")
        {
            input.OutputSnapshot = WorkflowJson.Write(new { productId = run.ProductId });
            input.Status = "succeeded";
            input.Progress = 100;
        }
        Record(listing, await listingContent.GetOrGenerateAsync(run.ProductId, ct, listingStream?.For(run.ProductId)));
    }

    private void Record(WorkflowNodeRun listing, Result<ListingContentResponseDto> result)
    {
        listing.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (result.IsFailure)
        {
            listing.Status = "failed";
            listing.Stage = null;
            listing.Progress = 0;
            listing.ErrorMessage = result.Error.Message;
            return;
        }
        listing.OutputSnapshot = WorkflowJson.Write(result.Value);
        listing.Status = "succeeded";
        listing.Stage = "Generated";
        listing.Progress = 100;
        listing.ErrorMessage = null;
    }

    // The listing is all such a run makes, so its outcome is the run's.
    private void FinishListingOnlyRun(WorkflowRun run, WorkflowNodeRun listing)
    {
        if (listing.Status == "succeeded")
        {
            run.Status = "completed";
            run.ErrorMessage = null;
            run.CompletedAt = time.GetUtcNow().UtcDateTime;
        }
        else if (listing.Status == "failed")
        {
            run.Status = "failed";
            run.ErrorMessage = listing.ErrorMessage;
        }
    }

    /// <summary>
    /// Mockup Approval in auto mode: approves this run's mock-ups waiting for review (of its design, when it has one)
    /// and passes the step when the video has an image to start from. AI scenes go on too: their cost was shown when
    /// the seller pressed Run. False keeps the run waiting.
    /// </summary>
    private async Task<bool> AutoApproveAsync(WorkflowRun run, WorkflowDefinition definition, IReadOnlyList<WorkflowNodeRun> checkpoints,
        WorkflowNodeRun gate, CancellationToken ct)
    {
        var mockupConfig = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
        var stored = await assets.FindAsync(x => x.ProductId == run.ProductId && x.DeletedAt == null && x.ContentHash != null, ct);
        var design = run.ArtworkGroupKey ?? VideoStoryboardPlanner.KnownArtworkGroup(stored, mockupConfig.ArtworkGroupKey);
        MockupAutoApproval.Approve(design is null ? stored : stored.Where(x => x.ArtworkGroupKey == design).ToArray(),
            run.UserId, time.GetUtcNow().UtcDateTime);

        var generate = checkpoints.SingleOrDefault(x => x.NodeType == "generate-video");
        var videoConfig = generate is null ? null : WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
        var approved = await ApprovedAssetsAsync(run, mockupConfig, videoConfig, ct);
        if (approved.Count == 0) return false;
        gate.Status = "succeeded";
        gate.OutputSnapshot = WorkflowJson.Write(new { mockupIds = approved.Select(x => x.Id), revisions = approved.Select(x => x.MetadataRevision), automatic = true });
        return true;
    }

    public Task<IReadOnlyList<MockupImage>> ApprovedAssetsAsync(WorkflowRun run, MockupConfig config, CancellationToken ct) =>
        ApprovedAssetsAsync(run, config, null, ct);

    public async Task<IReadOnlyList<MockupImage>> ApprovedAssetsAsync(WorkflowRun run, MockupConfig config,
        GenerateVideoConfig? videoConfig, CancellationToken ct)
    {
        var owned = await assets.FindAsync(x => x.ProductId == run.ProductId && x.DeletedAt == null, ct);
        var group = VideoStoryboardPlanner.KnownArtworkGroup(owned, config.ArtworkGroupKey);
        var approved = owned.Where(x => x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision)
            .Where(x => group == null || x.ArtworkGroupKey == group).ToArray();
        var (referencedIds, directSceneIds) = ReferencedVideoAssetIds(videoConfig);
        foreach (var frame in owned.Where(asset => referencedIds.Contains(asset.Id)))
        {
            var metadata = AiStudioFrameIdentity.Read(frame.Regions);
            if (metadata is not null) referencedIds.Add(metadata.SourceMockupId);
        }

        var hasConfiguredSelection = config.MockupIds is { Count: > 0 };
        var selected = approved.Where(x => !hasConfiguredSelection || config.MockupIds!.Contains(x.Id) || referencedIds.Contains(x.Id)).ToArray();
        var selectedIds = selected.Select(x => x.Id).ToHashSet();
        var selectedBaseAssets = selected.Where(x => AiStudioFrameIdentity.Read(x.Regions) is null).ToArray();
        var availableFrames = approved.Where(frame =>
        {
            var metadata = AiStudioFrameIdentity.Read(frame.Regions);
            if (metadata is null) return false;
            // An explicitly selected approved keyframe is a standalone immutable scene image.
            // Its generation prompt, references and original source are provenance, not run blockers.
            if (directSceneIds.Contains(frame.Id) || referencedIds.Contains(frame.Id)) return true;
            var source = approved.FirstOrDefault(asset => asset.Id == metadata.SourceMockupId);
            var sourceSelected = source is not null && (!hasConfiguredSelection || config.MockupIds!.Contains(source.Id) || referencedIds.Contains(source.Id));
            return source is not null && (selectedIds.Contains(frame.Id) || sourceSelected) &&
                AiStudioFrameIdentity.IsCurrent(frame, source, metadata.Layout, metadata.AspectRatio, metadata.CustomPrompt,
                    metadata.TemplateCode, metadata.References?.Select(reference => reference.ContentHash).ToArray());
        });
        return selectedBaseAssets.Concat(availableFrames).DistinctBy(x => x.Id).ToArray();
    }

    private static (HashSet<Guid> ReferencedIds, HashSet<Guid> DirectSceneIds) ReferencedVideoAssetIds(GenerateVideoConfig? config)
    {
        var referenced = new HashSet<Guid>();
        var direct = new HashSet<Guid>();
        if (config is null) return (referenced, direct);

        if (config.AiScenes is { Count: > 0 })
        {
            foreach (var scene in config.AiScenes)
            {
                var usesStudioFrame = scene.StartFrameMode == "studio" ||
                    scene.StartFrameMode is null && scene.TemplateCode == "cinematic_product_showcase" && config.AiStudioMode == "studio";
                if (scene.MockupId is Guid sourceId && sourceId != Guid.Empty)
                {
                    referenced.Add(sourceId);
                    if (!usesStudioFrame) direct.Add(sourceId);
                }
                var frameId = scene.StudioFrameId ?? (scene.StartFrameMode is null ? config.AiStudioFrameId : null);
                if (usesStudioFrame && frameId is Guid selectedFrameId && selectedFrameId != Guid.Empty)
                    referenced.Add(selectedFrameId);
            }
        }
        else if (config.AiShotOptions is { } legacy)
        {
            if (legacy.MockupId is Guid sourceId && sourceId != Guid.Empty)
            {
                referenced.Add(sourceId);
                direct.Add(sourceId);
            }
            foreach (var id in legacy.FullSceneMockupIds ?? [])
            {
                if (id == Guid.Empty) continue;
                referenced.Add(id);
                direct.Add(id);
            }
        }

        if (config.AiStudioMode == "studio")
        {
            if (config.AiStudioSourceMockupId is Guid sourceId && sourceId != Guid.Empty) referenced.Add(sourceId);
            if (config.AiStudioFrameId is Guid frameId && frameId != Guid.Empty) referenced.Add(frameId);
        }
        if (config.Mode == "standard")
        {
            foreach (var id in (config.SelectedMockupIds ?? []).Concat(config.SceneOrder ?? [])
                         .Concat(config.StandardScenes?.Select(scene => scene.MockupId) ?? []))
            {
                if (id == Guid.Empty) continue;
                referenced.Add(id);
                direct.Add(id);
            }
        }
        return (referenced, direct);
    }

    /// <summary>The most model photo images a package carries, newest first; with the video's scenes the worker fetches at most 20 files.</summary>
    public const int MaximumExportedModelPhotos = 8;

    /// <summary>
    /// Adds the product's approved model photo images (a mock-up put on a person, see ModelPhotoMockupService) to a
    /// package manifest under "modelPhotos", apart from the ones the video already shows. A manifest without any is
    /// left exactly as before.
    /// </summary>
    /// <summary>
    /// The guide for listing the package on Etsy by hand. File names follow the media worker's: <c>mockup/mockup-01.png</c>
    /// in the order sent, <c>design/design-01.png</c>, and the approved video under <c>video/final/v001__&lt;id&gt;/</c>.
    /// </summary>
    private static string EtsyGuide(WorkflowRun run, Product? product, IReadOnlyList<ExportAssetSnapshot> mockups, IReadOnlyList<ExportAssetSnapshot> designs,
        PromoVideo? video, bool hasListing)
    {
        WorkflowDefinition? definition = null;
        try { definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot); }
        catch (JsonException) { }
        EtsyPublishConfig? etsy = null;
        if (definition?.Nodes.FirstOrDefault(x => x.Type == "publish-etsy") is { } etsyNode)
        {
            try { etsy = WorkflowJson.Read<EtsyPublishConfig>(etsyNode.Config.GetRawText()); }
            catch (JsonException) { }
        }
        var viaPrintify = definition?.Nodes.Any(x => x.Type == "publish-printify") == true;
        var photos = mockups.Select((x, index) => new ExportGuidePhoto($"mockup/mockup-{index + 1:00}.png", x.AiMade)).ToArray();
        var files = designs.Select((x, index) => $"design/design-{index + 1:00}.{(x.FileFormat ?? "png").ToLowerInvariant()}").ToArray();
        var videoFile = video is null ? null : $"video/final/v{video.VersionNumber:000}__{video.Id}/video.mp4";
        return ExportEtsyGuide.Build(product?.Name ?? "this product", product?.ProductType, photos, files, videoFile, hasListing, etsy, viaPrintify);
    }

    private async Task<string> WithModelPhotosAsync(string manifest, Guid productId, IReadOnlyCollection<PromoVideoScene> videoScenes, CancellationToken ct)
    {
        var inVideo = videoScenes.Select(scene => scene.MockupImageId).OfType<Guid>().ToHashSet();
        var photos = (await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null && x.Role == ModelPhotoMockupService.Role
                && x.SourceType == APCS.Application.Features.BatchMockups.Common.MockupSources.Generated && x.ContentHash != null
                && x.ApprovalStatus == "approved", ct))
            .Where(x => !inVideo.Contains(x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaximumExportedModelPhotos)
            .Select(x => new { mockupId = x.Id, sourceHash = x.ContentHash, source = $"model-photos/{x.Id}.png" })
            .ToArray();
        if (photos.Length == 0)
            return manifest;

        var node = System.Text.Json.Nodes.JsonNode.Parse(manifest)!.AsObject();
        node["modelPhotos"] = JsonSerializer.SerializeToNode(photos, WorkflowJson.Options);
        return node.ToJsonString(WorkflowJson.Options);
    }

    /// <summary>The video the seller approved at Review Video when its file has not been rendered yet; null when there is nothing to wait for.</summary>
    private async Task<PromoVideo?> ApprovedVideoWithoutFileAsync(IReadOnlyList<WorkflowNodeRun> checkpoints, CancellationToken ct)
    {
        var review = checkpoints.SingleOrDefault(x => x.NodeType == "review-video");
        if (review is not { Status: "succeeded" }) return null;
        VideoPointer pointer;
        try { pointer = WorkflowJson.Read<VideoPointer>(review.OutputSnapshot); }
        catch (JsonException) { return null; }
        var video = await videos.GetByIdAsync(pointer.VideoId, ct);
        return video is { ApprovalStatus: "approved" } && video.Status != DeferredRender.VideoCompleted ? video : null;
    }

    /// <summary>
    /// The final render failed for good. The video goes back to being ready to show (it is still approved, and the render is asked
    /// for again when a step needs it), and a step that was waiting for the file fails with the reason, to be retried there.
    /// </summary>
    private async Task FailFinalRenderAsync(WorkflowRun run, WorkflowNodeRun generate, string message, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            var video = await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(generate.OutputSnapshot).VideoId, ct);
            if (video is { Status: DeferredRender.VideoRendering }) { video.Status = DeferredRender.VideoReady; video.UpdatedAt = now; }
        }
        catch (JsonException) { /* A generate step without a video pointer has nothing to put back. */ }
        var waiting = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct)).FirstOrDefault(DeferredRender.IsWaiting);
        if (waiting is null) return;
        waiting.Status = "failed"; waiting.ErrorMessage = message; waiting.UpdatedAt = now;
        run.Status = "failed"; run.ErrorMessage = message; run.Revision++;
    }

    /// <summary>The mock-ups and AI clips the scenes of a video that was never rendered are made from; null when one of them is gone.</summary>
    private async Task<VideoExportSourceSnapshot?> PlannedSceneSourcesAsync(WorkflowRun run, PromoVideo video, Storyboard board, CancellationToken ct)
    {
        var mockups = new Dictionary<Guid, MockupImage>();
        foreach (var id in board.Scenes.Select(scene => scene.MockupId).Distinct())
        {
            var mockup = await assets.GetByIdAsync(id, ct);
            if (mockup is null) return null;
            mockups[id] = mockup;
        }
        AiRenderJobPayload? aiPayload = null;
        if (board.Mode == "ai_shot")
        {
            var generate = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).SingleOrDefault();
            var job = generate is null ? null : (await jobs.FindAsync(x => x.WorkflowNodeRunId == generate.Id && x.Kind == "render", ct)).SingleOrDefault();
            if (job is null) return null;
            try { aiPayload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload); }
            catch (JsonException) { return null; }
            if (aiPayload.VideoId != video.Id) return null;
        }
        return VideoExportSources.Build(video.Id, board, aiPayload, mockups);
    }

    /// <summary>With the render deferred: the video's scenes are all available, so Generate Video is done and the video is ready to be shown.</summary>
    private void MarkReady(WorkflowNodeRun node, PromoVideo video)
    {
        var now = time.GetUtcNow().UtcDateTime;
        video.Status = DeferredRender.VideoReady; video.UpdatedAt = now;
        node.Status = "succeeded"; node.Progress = 100; node.Stage = null; node.ErrorMessage = null; node.UpdatedAt = now;
    }

    /// <summary>The API has just stored the last AI clip of a video whose final render is deferred: Generate Video ends here.</summary>
    public async Task FinishGenerateWithClipsAsync(WorkflowRun run, WorkflowNodeRun generate, CancellationToken ct)
    {
        PromoVideo? video = null;
        try { video = await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(generate.OutputSnapshot).VideoId, ct); }
        catch (JsonException) { /* The step points at no video. */ }
        if (video is null)
        {
            await FailAsync(run, generate.Id, "The video this step was making could not be found. Generate the video again.", ct);
            return;
        }
        MarkReady(generate, video);
    }

    /// <summary>
    /// Queues the render of the final video, once, for a video whose scenes are ready: the job that waited for this is handed to the
    /// media worker. Safe to call again: a render already queued or running is left alone, and one that failed or was cancelled is
    /// queued afresh. False when there is nothing to render (the job is gone or belongs to another version of the video).
    /// </summary>
    public async Task<Result> RequestFinalRenderAsync(WorkflowRun run, PromoVideo video, CancellationToken ct)
    {
        var unavailable = Result.Failure(Error.Conflict("FinalRenderUnavailable",
            "The final video cannot be made: its scenes are no longer available. Generate the video again."));
        if (video.Status == DeferredRender.VideoCompleted) return Result.Success();
        var generate = (await nodes.FindAsync(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video", ct)).SingleOrDefault();
        if (generate is null) return unavailable;
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == generate.Id && x.Kind == "render", ct)).SingleOrDefault();
        if (job is null) return unavailable;
        try { if (WorkflowJson.Read<RenderJobPayload>(job.Payload).VideoId != video.Id) return unavailable; }
        catch (JsonException) { return unavailable; }
        var now = time.GetUtcNow().UtcDateTime;
        switch (job.Status)
        {
            case DeferredRender.JobStatus or "failed" or "cancelled":
                // A ready video is not counted against the plan: it is when its file is made, so the plan is asked now.
                var allowed = await CheckPlanForRenderAsync(run.UserId, video, ct);
                if (allowed.IsFailure) return allowed;
                job.Status = "queued"; job.Attempt = 0; job.Progress = 0; job.Stage = null; job.LeaseToken = null; job.LeaseExpiresAt = null;
                job.ErrorMessage = null; job.AvailableAt = now; job.UpdatedAt = now;
                video.Status = DeferredRender.VideoRendering; video.UpdatedAt = now;
                return Result.Success();
            case "queued" or "leased":
                if (video.Status != DeferredRender.VideoRendering) { video.Status = DeferredRender.VideoRendering; video.UpdatedAt = now; }
                return Result.Success();
            default:
                return unavailable;
        }
    }

    /// <summary>Standard videos count towards the plan's video allowance and daily cap once their file is made; AI videos have their own feature check.</summary>
    private async Task<Result> CheckPlanForRenderAsync(Guid userId, PromoVideo video, CancellationToken ct)
    {
        if (entitlements is null || video.Mode != "standard") return Result.Success();
        var allowance = await entitlements.RequireAllowanceAsync(userId, Features.Entitlements.UsageAllowances.Videos, 1, ct);
        if (allowance.IsFailure) return allowance;
        return await entitlements.RequireDailyCapacityAsync(userId, Features.Entitlements.PlanFeatures.DailyVideoCap, 1, ct);
    }

    /// <summary>Puts a step that needs the final video to wait for it: running, with the render asked for.</summary>
    private async Task WaitForFinalRenderAsync(WorkflowRun run, WorkflowNodeRun node, PromoVideo video, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var requested = await RequestFinalRenderAsync(run, video, ct);
        if (requested.IsFailure)
        {
            node.Status = "failed"; node.ErrorMessage = requested.Error.Message;
            run.Status = "failed"; run.ErrorMessage = node.ErrorMessage; run.Revision++; node.UpdatedAt = now;
            return;
        }
        node.Status = "running"; node.Stage = DeferredRender.WaitStage; node.ErrorMessage = null; node.UpdatedAt = now;
        node.Progress = video.Status == DeferredRender.VideoRendering ? Math.Max(node.Progress, 0) : 0;
        node.OutputSnapshot = WorkflowJson.Write(new AwaitingRenderState(true, video.Id));
    }

    private async Task EnqueueAsync<T>(WorkflowRun run, WorkflowNodeRun node, string kind, T payload, CancellationToken ct, string status = "queued")
    {
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id && x.Kind == kind, ct)).SingleOrDefault();
        if (job == null)
        {
            job = new() { Id = Guid.NewGuid(), WorkflowRunId = run.Id, WorkflowNodeRunId = node.Id, UserId = run.UserId, MaximumAttempts = 3, CreatedAt = time.GetUtcNow().UtcDateTime };
            await jobs.AddAsync(job, cancellationToken: ct);
        }
        job.Kind = kind; job.Payload = WorkflowJson.Write(payload); job.Status = status; job.Attempt = 0; job.Progress = 0; job.LeaseToken = null;
        job.LeaseExpiresAt = null; job.ErrorMessage = null; job.AvailableAt = time.GetUtcNow().UtcDateTime; job.UpdatedAt = job.AvailableAt;
        if (status == AiClipPhase.JobStatus) { job.Stage = AiClipPhase.Stage; job.Progress = AiClipPhase.StartProgress; }
        logger?.LogInformation("Media job {JobId} ({Kind}) queued for run {RunId} as {Status}.", job.Id, kind, run.Id, status);
    }
    public async Task FailAsync(WorkflowRun run, Guid nodeId, string message, CancellationToken ct)
    {
        var node = (await nodes.GetByIdAsync(nodeId, ct))!;
        // The final render of a video whose Generate Video step already ended: that step stays done, and the step waiting for the file fails.
        if (node.NodeType == "generate-video" && node.Status == "succeeded")
        {
            await FailFinalRenderAsync(run, node, message, ct);
            return;
        }
        node.Status = "failed"; node.ErrorMessage = message; run.Status = "failed"; run.ErrorMessage = message; run.Revision++;
        if (node.NodeType == "generate-video") { var v = await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(node.OutputSnapshot).VideoId, ct); if (v != null) v.Status = "failed"; }
    }
}
