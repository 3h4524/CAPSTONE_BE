using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.ListingContent;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowRuntime(IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<VideoTemplate> templates, IRepository<MediaJob> jobs, IRepository<ExportPackage> exports,
    IRepository<ExportPackageItem> exportItems, VideoStoryboardPlanner planner, IListingContentService listingContent, TimeProvider time,
    IVideoExperienceContextService? experiences = null)
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
        return planner.Plan(product, approved, config, aiTemplates, experienceMap);
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
                case "approval-gate": node.Status = "waiting_for_input"; run.Status = "waiting_for_input"; return;
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
                        var oldJob = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id, ct)).SingleOrDefault();
                        if (oldJob?.Kind == "render")
                        {
                            try { retained = WorkflowJson.Read<AiRenderJobPayload>(oldJob.Payload).AiShotStates?.ToArray() ?? []; }
                            catch (JsonException) { /* The previous render used the Standard payload. */ }
                        }
                        // A scene Google answered without a clip starts over, so this attempt submits it again (the seller
                        // has just confirmed its cost). One whose submission is uncertain is kept: it is never sent twice.
                        var states = (board.AiShots ?? []).Select(shot => retained.FirstOrDefault(x => x.Fingerprint == shot.Fingerprint && x.Status != "failed")
                            is { } existing ? existing with { SceneOrder = shot.SceneOrder } : new AiShotJobState(shot.SceneOrder, shot.Fingerprint)).ToArray();
                        await EnqueueAsync(run, node, "render", new AiRenderJobPayload(video.Id, board, states), ct);
                    }
                    else await EnqueueAsync(run, node, "render", new RenderJobPayload(video.Id, board), ct);
                    return;
                case "review-video":
                    node.OutputSnapshot = checkpoints.Single(x => x.NodeType == "generate-video").OutputSnapshot;
                    node.Status = "waiting_for_review"; run.Status = "waiting_for_review"; return;
                case "export-zip":
                    var pointer = WorkflowJson.Read<VideoPointer>(checkpoints.Single(x => x.NodeType == "review-video").OutputSnapshot);
                    var approvedVideo = (await videos.GetByIdAsync(pointer.VideoId, ct))!;
                    if (approvedVideo.ApprovalStatus != "approved") { node.Status = "failed"; run.Status = "failed"; run.ErrorMessage = "Export requires the exact approved video."; return; }
                    var videoScenes = (await scenes.FindAsync(x => x.PromoVideoId == approvedVideo.Id, ct)).OrderBy(x => x.SceneOrder).ToArray();
                    var manifest = new { schemaVersion = 1, productId = run.ProductId, videoId = approvedVideo.Id, version = approvedVideo.VersionNumber,
                        mode = approvedVideo.Mode, fingerprint = approvedVideo.Fingerprint, approvedRevision = approvedVideo.ReviewRevision,
                        video = "video.mp4", thumbnail = "thumbnail.png", scenes = videoScenes.Select(s => new { order = s.SceneOrder, mockupId = s.MockupImageId,
                            sourceRevision = s.SourceRevision, sourceHash = s.SourceHash, generationStrategy = s.GenerationStrategy, source = $"mockups/{s.MockupImageId}.png" }) };
                    var package = new ExportPackage { Id = Guid.NewGuid(), UserId = run.UserId, WorkflowRunId = run.Id, Type = "zip_package",
                        Name = "Approved video package", Status = "preparing", Manifest = WorkflowJson.Write(manifest), CreatedAt = time.GetUtcNow().UtcDateTime, ExpiresAt = time.GetUtcNow().AddDays(7).UtcDateTime };
                    await exports.AddAsync(package, cancellationToken: ct);
                    await exportItems.AddAsync(new() { Id = Guid.NewGuid(), ExportPackageId = package.Id, ProductId = run.ProductId, PromoVideoId = approvedVideo.Id,
                        IncludePromoVideo = true, IncludeMockupImages = true, IncludeDesignImages = false, IncludeListingContent = false, ItemStatus = "pending", CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    node.OutputSnapshot = WorkflowJson.Write(new { exportId = package.Id }); node.Status = "running";
                    await EnqueueAsync(run, node, "export_zip", new ExportJobPayload(package.Id, approvedVideo.Id, approvedVideo.StorageKey!, approvedVideo.StorageVersion!,
                        approvedVideo.ThumbnailStorageKey!, approvedVideo.ThumbnailStorageVersion!, JsonSerializer.Deserialize<JsonElement>(package.Manifest)), ct); return;
            }
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
    private async Task EnqueueAsync<T>(WorkflowRun run, WorkflowNodeRun node, string kind, T payload, CancellationToken ct)
    {
        var job = (await jobs.FindAsync(x => x.WorkflowNodeRunId == node.Id, ct)).SingleOrDefault();
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
