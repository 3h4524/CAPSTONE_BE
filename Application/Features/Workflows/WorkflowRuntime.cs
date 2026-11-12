using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowRuntime(IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<VideoTemplate> templates, IRepository<MediaJob> jobs, IRepository<ExportPackage> exports,
    IRepository<ExportPackageItem> exportItems, VideoStoryboardPlanner planner, IListingContentService listingContent, TimeProvider time,
    IVideoExperienceContextService? experiences = null, IVideoPromptPreparationService? promptPreparation = null,
    IRepository<DesignImage>? designImages = null,
    Abstractions.BackgroundJobs.IWorkflowPublishQueue? publishQueue = null,
    IRepository<MockupTemplate>? mockupTemplates = null)
{
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
        var planned = planner.Plan(product, approved, config, aiTemplates, experienceMap);
        if (planned.IsFailure || promptPreparation is null) return planned;
        return Result.Success(await promptPreparation.PrepareAsync(run.UserId, planned.Value, approved, ct));
    }

    public async Task AdvanceAsync(WorkflowRun run, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct);
        var listing = checkpoints.SingleOrDefault(x => x.NodeType == "generate-listing");
        if (listing is { Status: "pending" }) await GenerateListingAsync(run, checkpoints, listing, ct);
        if (!definition.Nodes.Any(x => x.Type == "generate-video"))
        {
            if (listing is not null) FinishListingOnlyRun(run, listing);
            return;
        }
        // A listing that could not be written stays failed on its own step and can be retried there:
        // the video is made from the mock-ups and does not wait for it.
        foreach (var type in WorkflowCapabilityRegistry.Pipeline)
        {
            var node = checkpoints.Single(x => x.NodeType == type);
            if (node.Status == "succeeded") continue;
            if (node.Status is "running" or "failed" or "cancelled") return;
            node.UpdatedAt = time.GetUtcNow().UtcDateTime;
            switch (type)
            {
                case "product-input": node.OutputSnapshot = WorkflowJson.Write(new { productId = run.ProductId }); node.Status = "succeeded"; break;
                case "apply-mockup": node.Status = "succeeded"; break;
                case "approval-gate":
                    // Auto mode approves the mock-ups itself and goes on, unless the video still needs the seller.
                    if (MockupAutoApproval.IsAuto(definition) && await AutoApproveAsync(run, definition, checkpoints, node, ct)) break;
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
                        SourceHash = scene.SourceHash, SceneConfig = WorkflowJson.Write(scene), Warnings = WorkflowJson.Write(scene.Warnings), TransitionEffect = scene.Transition,
                        TextOverlayContent = scene.Text, CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    node.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id)); node.Status = "running"; node.Stage = "Planning"; node.Progress = 5;
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
                            return existing is not null
                                ? existing with { SceneOrder = shot.SceneOrder, Fingerprint = shot.Fingerprint }
                                : new AiShotJobState(shot.SceneOrder, shot.Fingerprint);
                        }).ToArray();
                        await EnqueueAsync(run, node, "render", new AiRenderJobPayload(video.Id, board, states), ct);
                    }
                    else await EnqueueAsync(run, node, "render", new RenderJobPayload(video.Id, board), ct);
                    return;
                case "review-video":
                    node.OutputSnapshot = checkpoints.Single(x => x.NodeType == "generate-video").OutputSnapshot;
                    // Auto mode approves the finished video itself and goes on to export, unless it cannot be listed.
                    if (VideoAutoApproval.IsAuto(definition) &&
                        await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(node.OutputSnapshot).VideoId, ct) is { } candidate &&
                        VideoAutoApproval.Approve(candidate, run.UserId, time.GetUtcNow().UtcDateTime))
                    {
                        node.Status = "succeeded";
                        break;
                    }
                    node.Status = "waiting_for_review"; run.Status = "waiting_for_review"; return;
                case "export-zip":
                    var pointer = WorkflowJson.Read<VideoPointer>(checkpoints.Single(x => x.NodeType == "review-video").OutputSnapshot);
                    var approvedVideo = (await videos.GetByIdAsync(pointer.VideoId, ct))!;
                    if (approvedVideo.ApprovalStatus != "approved") { node.Status = "failed"; run.Status = "failed"; run.ErrorMessage = "Export requires the exact approved video."; return; }
                    var videoScenes = (await scenes.FindAsync(x => x.PromoVideoId == approvedVideo.Id, ct)).OrderBy(x => x.SceneOrder).ToArray();
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
                    try
                    {
                        using var snapshotDocument = JsonDocument.Parse(approvedVideo.ConfigSnapshot ?? "{}");
                        exportStoryboard = snapshotDocument.RootElement.GetProperty("storyboard").Deserialize<Storyboard>(WorkflowJson.Options);
                        if (snapshotDocument.RootElement.TryGetProperty("exportSources", out var sourceElement))
                            sceneSnapshot = sourceElement.Deserialize<VideoExportSourceSnapshot>(WorkflowJson.Options);
                    }
                    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentNullException) { }
                    var manifest = new { schemaVersion = 1, productId = run.ProductId, videoId = approvedVideo.Id, version = approvedVideo.VersionNumber,
                        mode = approvedVideo.Mode, fingerprint = approvedVideo.Fingerprint, approvedRevision = approvedVideo.ReviewRevision,
                        video = "video.mp4", thumbnail = "thumbnail.png", scenes = videoScenes.Select(s => new { order = s.SceneOrder, mockupId = s.MockupImageId,
                            sourceRevision = s.SourceRevision, sourceHash = s.SourceHash, generationStrategy = s.GenerationStrategy, source = $"mockups/{s.MockupImageId}.png" }) };
                    var package = new ExportPackage { Id = Guid.NewGuid(), UserId = run.UserId, WorkflowRunId = run.Id, Type = "zip_package",
                        Name = "Approved video package", Status = "preparing", Manifest = await WithModelPhotosAsync(WorkflowJson.Write(manifest), run.ProductId, videoScenes, ct),
                        CreatedAt = time.GetUtcNow().UtcDateTime, ExpiresAt = time.GetUtcNow().AddDays(7).UtcDateTime };
                    await exports.AddAsync(package, cancellationToken: ct);
                    await exportItems.AddAsync(new() { Id = Guid.NewGuid(), ExportPackageId = package.Id, ProductId = run.ProductId, PromoVideoId = approvedVideo.Id,
                        IncludePromoVideo = true, IncludeMockupImages = approvedMockups.Length > 0, IncludeDesignImages = approvedDesigns.Length > 0,
                        IncludeListingContent = exportListing is not null, ItemStatus = "pending", CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    var guide = EtsyGuide(run, exportProduct, approvedMockups, approvedDesigns, approvedVideo, exportListing is not null);
                    node.OutputSnapshot = WorkflowJson.Write(new { exportId = package.Id }); node.Status = "running";
                    await EnqueueAsync(run, node, "export_zip", new ExportJobPayload(package.Id, approvedVideo.Id, approvedVideo.StorageKey!, approvedVideo.StorageVersion!,
                        approvedVideo.ThumbnailStorageKey!, approvedVideo.ThumbnailStorageVersion!, JsonSerializer.Deserialize<JsonElement>(package.Manifest),
                        exportProduct?.Name, exportProduct?.ProductType, approvedMockups, approvedDesigns, exportListing, exportStoryboard, sceneSnapshot, guide), ct); return;
            }
        }
        // Publishing calls Printify and Etsy for up to minutes, so it runs in the background after this transaction
        // commits; WorkflowPublishService finishes the step and advances the run again.
        foreach (var type in WorkflowCapabilityRegistry.PublishSteps)
        {
            var node = checkpoints.SingleOrDefault(x => x.NodeType == type);
            if (node is null || node.Status == "succeeded") continue;
            if (node.Status is "running" or "failed" or "cancelled") return;
            node.Status = "running"; node.Stage = "Queued"; node.Progress = 5; node.ErrorMessage = null;
            node.OutputSnapshot = WorkflowJson.Write(new PublishNodeState("queued"));
            node.UpdatedAt = time.GetUtcNow().UtcDateTime;
            publishQueue?.Enqueue(node.Id);
            return;
        }
        run.Status = "completed"; run.CompletedAt = time.GetUtcNow().UtcDateTime;
    }
    /// <summary>Writes the listing again after it failed, without touching the video steps.</summary>
    public async Task RetryListingAsync(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> checkpoints, CancellationToken ct)
    {
        var listing = checkpoints.Single(x => x.NodeType == "generate-listing");
        listing.Attempt++;
        await GenerateListingAsync(run, checkpoints, listing, ct);
        if (checkpoints.All(x => x.NodeType != "generate-video")) FinishListingOnlyRun(run, listing);
    }

    private async Task GenerateListingAsync(WorkflowRun run, IReadOnlyList<WorkflowNodeRun> checkpoints, WorkflowNodeRun listing, CancellationToken ct)
    {
        var input = checkpoints.Single(x => x.NodeType == "product-input");
        if (input.Status != "succeeded")
        {
            input.OutputSnapshot = WorkflowJson.Write(new { productId = run.ProductId });
            input.Status = "succeeded";
            input.Progress = 100;
        }
        var result = await listingContent.GenerateAsync(run.ProductId, ct);
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
    /// and passes the step when the video has an image and needs no paid confirmation. False keeps the run waiting.
    /// </summary>
    private async Task<bool> AutoApproveAsync(WorkflowRun run, WorkflowDefinition definition, IReadOnlyList<WorkflowNodeRun> checkpoints,
        WorkflowNodeRun gate, CancellationToken ct)
    {
        var mockupConfig = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
        var stored = await assets.FindAsync(x => x.ProductId == run.ProductId && x.DeletedAt == null && x.ContentHash != null, ct);
        var design = run.ArtworkGroupKey ?? VideoStoryboardPlanner.KnownArtworkGroup(stored, mockupConfig.ArtworkGroupKey);
        MockupAutoApproval.Approve(design is null ? stored : stored.Where(x => x.ArtworkGroupKey == design).ToArray(),
            run.UserId, time.GetUtcNow().UtcDateTime);

        var generate = checkpoints.Single(x => x.NodeType == "generate-video");
        var videoConfig = WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot);
        if (!MockupAutoApproval.NeedsNoConsent(videoConfig)) return false;
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
        PromoVideo video, bool hasListing)
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
        var videoFile = $"video/final/v{video.VersionNumber:000}__{video.Id}/video.mp4";
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

    private async Task EnqueueAsync<T>(WorkflowRun run, WorkflowNodeRun node, string kind, T payload, CancellationToken ct)
    {
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id && x.Kind == kind, ct)).SingleOrDefault();
        if (job == null)
        {
            job = new() { Id = Guid.NewGuid(), WorkflowRunId = run.Id, WorkflowNodeRunId = node.Id, UserId = run.UserId, MaximumAttempts = 3, CreatedAt = time.GetUtcNow().UtcDateTime };
            await jobs.AddAsync(job, cancellationToken: ct);
        }
        job.Kind = kind; job.Payload = WorkflowJson.Write(payload); job.Status = "queued"; job.Attempt = 0; job.Progress = 0; job.LeaseToken = null;
        job.LeaseExpiresAt = null; job.ErrorMessage = null; job.AvailableAt = time.GetUtcNow().UtcDateTime; job.UpdatedAt = job.AvailableAt;
    }
    public async Task FailAsync(WorkflowRun run, Guid nodeId, string message, CancellationToken ct)
    {
        var node = (await nodes.GetByIdAsync(nodeId, ct))!;
        node.Status = "failed"; node.ErrorMessage = message; run.Status = "failed"; run.ErrorMessage = message; run.Revision++;
        if (node.NodeType == "generate-video") { var v = await videos.GetByIdAsync(WorkflowJson.Read<VideoPointer>(node.OutputSnapshot).VideoId, ct); if (v != null) v.Status = "failed"; }
    }
}
