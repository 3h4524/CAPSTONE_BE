using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed class WorkflowRuntime(IRepository<WorkflowNodeRun> nodes, IRepository<Product> products,
    IRepository<MockupImage> assets, IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes,
    IRepository<VideoTemplate> templates, IRepository<MediaJob> jobs, IRepository<ExportPackage> exports,
    IRepository<ExportPackageItem> exportItems, VideoStoryboardPlanner planner, TimeProvider time)
{
    public async Task<Result<Storyboard>> PlanAsync(WorkflowRun run, GenerateVideoConfig config, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var mockupConfig = WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup"));
        var approved = await ApprovedAssetsAsync(run, mockupConfig, ct);
        var product = await products.GetByIdAsync(run.ProductId, ct);
        return product is null
            ? Result.Failure<Storyboard>(Error.NotFound("ProductNotFound", "Product not found."))
            : planner.Plan(product, approved, config);
    }

    public async Task AdvanceAsync(WorkflowRun run, CancellationToken ct)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var checkpoints = await nodes.FindAsync(x => x.WorkflowRunId == run.Id, ct);
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
                    if (plan.IsFailure) { node.Status = "waiting_for_input"; run.Status = "waiting_for_input"; node.ErrorMessage = plan.Error.Message; return; }
                    var board = plan.Value;
                    var previous = (await videos.FindAsync(x => x.WorkflowRunId == run.Id, ct)).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
                    var series = previous?.SeriesId ?? Guid.NewGuid();
                    var reuse = previous is { Status: "failed" } && previous.Fingerprint == board.Fingerprint && node.Attempt == previous.VersionNumber;
                    var video = reuse ? previous! : new PromoVideo { Id = Guid.NewGuid(), ProductId = run.ProductId, WorkflowRunId = run.Id,
                        VideoTemplateId = (await templates.FindAsync(x => x.Code == board.Template && x.TemplateVersion == board.TemplateVersion, ct)).Single().Id,
                        Mode = "standard", SeriesId = series, VersionNumber = (previous?.VersionNumber ?? 0) + 1, ReviewRevision = 1,
                        Fingerprint = board.Fingerprint, TemplateVersion = board.TemplateVersion, ConfigSnapshot = WorkflowJson.Write(new { config, storyboard = board }),
                        QaResult = "{}", VideoDurationSeconds = config.DurationSeconds, VideoResolution = $"{board.Width}x{board.Height}", AspectRatio = board.AspectRatio,
                        FileFormat = "mp4", PlatformTarget = "etsy", Status = "queued", ApprovalStatus = "pending", IsFinal = false, CreatedAt = time.GetUtcNow().UtcDateTime };
                    if (!reuse) await videos.AddAsync(video, cancellationToken: ct);
                    video.Status = "queued";
                    foreach (var scene in reuse ? Array.Empty<ScenePlan>() : board.Scenes) await scenes.AddAsync(new() { Id = Guid.NewGuid(), PromoVideoId = video.Id, MockupImageId = scene.MockupId,
                        SceneOrder = scene.SceneOrder + 1, DurationSeconds = scene.DurationFrames / 30m, GenerationStrategy = "standard", SourceRevision = scene.SourceRevision,
                        SourceHash = scene.SourceHash, SceneConfig = WorkflowJson.Write(scene), Warnings = WorkflowJson.Write(scene.Warnings), TransitionEffect = scene.Transition,
                        TextOverlayContent = scene.Text, CreatedAt = time.GetUtcNow().UtcDateTime }, cancellationToken: ct);
                    node.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id)); node.Status = "running"; node.Stage = "Planning"; node.Progress = 5;
                    await EnqueueAsync(run, node, "render", new RenderJobPayload(video.Id, board), ct); return;
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
    public async Task<IReadOnlyList<MockupImage>> ApprovedAssetsAsync(WorkflowRun run, MockupConfig config, CancellationToken ct)
    {
        var selected = await assets.FindAsync(x => x.ProductId == run.ProductId && x.DeletedAt == null && x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision, ct);
        return selected.Where(x => config.MockupIds is not { Count: > 0 } || config.MockupIds.Contains(x.Id)).Where(x => string.IsNullOrEmpty(config.ArtworkGroupKey) || x.ArtworkGroupKey == config.ArtworkGroupKey).Take(8).ToArray();
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
