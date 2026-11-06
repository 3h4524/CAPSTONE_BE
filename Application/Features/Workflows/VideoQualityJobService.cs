using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Owns durable automatic evaluation jobs for generated AI scenes.</summary>
public interface IVideoQualityJobService
{
    Task QueueAsync(Guid workflowRunId, Guid videoId, int? sceneOrder, CancellationToken cancellationToken);
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}

public sealed class VideoQualityJobService(IRepository<WorkflowRun> runs, IRepository<WorkflowNodeRun> nodes,
    IRepository<MediaJob> jobs, IRepository<PromoVideo> videos, IRepository<MockupImage> assets,
    IWorkflowStateRepository state, IUnitOfWork unitOfWork, IMediaStorage storage,
    IVideoQualityEvidenceRunner evidenceRunner, TimeProvider time, IVideoQualityLeaseHeartbeat? heartbeat = null) : IVideoQualityJobService
{
    public async Task QueueAsync(Guid workflowRunId, Guid videoId, int? sceneOrder, CancellationToken cancellationToken)
    {
        var run = await runs.GetByIdAsync(workflowRunId, cancellationToken);
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (run is null || video?.WorkflowRunId != workflowRunId || video.Mode != "ai_shot") return;

        var generate = (await nodes.FindAsync(node => node.WorkflowRunId == workflowRunId && node.NodeType == "generate-video", cancellationToken))
            .SingleOrDefault();
        if (generate is null) return;

        var active = await jobs.FindAsync(job => job.WorkflowNodeRunId == generate.Id && job.Kind == "video_quality" &&
            (job.Status == "queued" || job.Status == "leased"), cancellationToken);
        if (active.Any(job => Targets(job.Payload, videoId, sceneOrder))) return;

        var targets = await FindTargetsAsync(generate.Id, videoId, sceneOrder, cancellationToken);
        if (targets.Count == 0) return;
        var orders = targets.Select(target => target.SceneOrder).ToArray();

        var payload = AutomaticVideoQualityPayload.Read(video.QaResult).Queue(orders, time.GetUtcNow());
        video.QaResult = AutomaticVideoQualityPayload.Write(video.QaResult, payload);
        video.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await jobs.AddAsync(new MediaJob
        {
            Id = Guid.NewGuid(),
            WorkflowRunId = workflowRunId,
            WorkflowNodeRunId = generate.Id,
            UserId = run.UserId,
            Kind = "video_quality",
            Status = "queued",
            Payload = WorkflowJson.Write(new VideoQualityJobPayload(videoId, sceneOrder, targets)),
            Attempt = 0,
            MaximumAttempts = 3,
            AvailableAt = time.GetUtcNow().UtcDateTime,
            Progress = 0,
            Stage = "Queued",
            CreatedAt = time.GetUtcNow().UtcDateTime,
            UpdatedAt = time.GetUtcNow().UtcDateTime,
        }, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        MediaJob? job;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            job = await state.ClaimAsync(time.GetUtcNow().UtcDateTime, Guid.NewGuid(), "video_quality", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        if (job is null) return false;

        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = heartbeat is not null && job.LeaseToken is Guid token
            ? heartbeat.RunAsync(job.Id, token, heartbeatCancellation.Token) : Task.CompletedTask;
        try
        {
            var payload = WorkflowJson.Read<VideoQualityJobPayload>(job.Payload);
            await EvaluateAsync(job, payload, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RequeueOrFailAsync(job, exception, cancellationToken);
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try { await heartbeatTask; } catch (OperationCanceledException) { }
        }
        return true;
    }

    private async Task EvaluateAsync(MediaJob job, VideoQualityJobPayload request, CancellationToken cancellationToken)
    {
        var video = await videos.GetByIdAsync(request.VideoId, cancellationToken);
        if (video is null || video.WorkflowRunId != job.WorkflowRunId)
        {
            await CompleteAsync(job, cancellationToken);
            return;
        }

        var targets = request.SceneTargets?.Where(target => request.SceneOrder is null || target.SceneOrder == request.SceneOrder.Value)
            .OrderBy(target => target.SceneOrder).ToArray()
            ?? (await FindTargetsAsync(job.WorkflowNodeRunId, request.VideoId, request.SceneOrder, cancellationToken)).ToArray();
        if (targets.Length == 0)
        {
            await WriteTerminalAsync(job, video, request, "skipped", "This video has no matching AI scene.", cancellationToken);
            return;
        }

        var quality = AutomaticVideoQualityPayload.Read(video.QaResult).MarkRunning(targets.Select(shot => shot.SceneOrder), time.GetUtcNow());
        video.QaResult = AutomaticVideoQualityPayload.Write(video.QaResult, quality);
        video.UpdatedAt = time.GetUtcNow().UtcDateTime;
        job.Stage = "Evaluating AI scenes";
        job.Progress = 5;
        job.HeartbeatAt = time.GetUtcNow().UtcDateTime;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var pending = new List<VideoQualityEvidenceRequest>();
        var results = new Dictionary<int, VideoQualityEvidenceResult>();
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.StorageKey) || string.IsNullOrWhiteSpace(target.StorageVersion))
            {
                results[target.SceneOrder] = new("skipped", "video-quality-v1", new Dictionary<string, object?>(),
                    "The AI scene does not have a stored clip.");
            }
            else
            {
                var source = await assets.GetByIdAsync(target.MockupId, cancellationToken);
                if (source is null || string.IsNullOrWhiteSpace(source.StorageKey))
                {
                    results[target.SceneOrder] = new("skipped", "video-quality-v1", new Dictionary<string, object?>(),
                        "The approved source image is no longer available.");
                }
                else
                {
                    var sourceBytes = await storage.ReadImageAsync(source.StorageKey, source.StorageVersion, cancellationToken);
                    var reference = await VideoQualityReferenceMockup.ReadAsync(source, assets, storage, cancellationToken);
                    VideoQualityInputGeometry? geometry = null;
                    if (target.ProviderKey == "minimax" && target.SourceHash is not null &&
                        target.SourceHash == source.ContentHash && target.InputAspectRatio is "9:16" or "16:9")
                    {
                        Region? artwork = null;
                        try { artwork = WorkflowJson.Read<MockupRegions>(source.Regions).Artwork; }
                        catch (JsonException) { }
                        static VideoQualityRegion? Map(Region? region) => region is null ? null :
                            new VideoQualityRegion(region.X, region.Y, region.Width, region.Height);
                        geometry = new(target.InputAspectRatio, Map(target.SourceCrop), Map(artwork));
                    }
                    pending.Add(new(storage.SignRead(target.StorageKey, "video", target.StorageVersion), sourceBytes, target.SceneOrder, reference, geometry));
                }
            }

        }

        if (pending.Count > 0)
            foreach (var result in await evidenceRunner.EvaluateBatchAsync(pending, cancellationToken)) results[result.Key] = result.Value;

        var completed = 0;
        foreach (var target in targets)
        {
            var evidence = results.GetValueOrDefault(target.SceneOrder) ?? new VideoQualityEvidenceResult("failed", "video-quality-v1",
                new Dictionary<string, object?>(), "The evaluator returned no result for this scene.");
            quality = AutomaticVideoQualityPayload.Read(video.QaResult).Complete(target.SceneOrder,
                new VideoSceneEvaluationResponse(evidence.Status, time.GetUtcNow(), evidence.EvaluatorVersion, evidence.Metrics, evidence.Detail));
            video.QaResult = AutomaticVideoQualityPayload.Write(video.QaResult, quality);
            video.UpdatedAt = time.GetUtcNow().UtcDateTime;
            completed++;
            job.Progress = Math.Min(95, 5 + (int)Math.Round(90d * completed / targets.Length));
            job.HeartbeatAt = time.GetUtcNow().UtcDateTime;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await CompleteAsync(job, cancellationToken);
    }

    private async Task WriteTerminalAsync(MediaJob job, PromoVideo video, VideoQualityJobPayload request, string status,
        string detail, CancellationToken cancellationToken)
    {
        var orders = await FindOrdersAsync(job.WorkflowNodeRunId, video.Id, request.SceneOrder, cancellationToken, request.SceneTargets);
        var quality = AutomaticVideoQualityPayload.Read(video.QaResult);
        foreach (var order in orders)
            quality = quality.Complete(order, new VideoSceneEvaluationResponse(status, time.GetUtcNow(), "video-quality-v1",
                new Dictionary<string, object?>(), detail));
        video.QaResult = AutomaticVideoQualityPayload.Write(video.QaResult, quality);
        video.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await CompleteAsync(job, cancellationToken);
    }

    private async Task CompleteAsync(MediaJob job, CancellationToken cancellationToken)
    {
        job.Status = "succeeded";
        job.Progress = 100;
        job.Stage = "Completed";
        job.ErrorMessage = null;
        job.LeaseToken = null;
        job.LeaseExpiresAt = null;
        job.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RequeueOrFailAsync(MediaJob job, Exception exception, CancellationToken cancellationToken)
    {
        job.LeaseToken = null;
        job.LeaseExpiresAt = null;
        job.ErrorMessage = "Automatic video-quality evaluation could not finish.";
        job.UpdatedAt = time.GetUtcNow().UtcDateTime;
        if (job.Attempt >= job.MaximumAttempts)
        {
            job.Status = "failed";
            job.Stage = "Failed";
            var request = WorkflowJson.Read<VideoQualityJobPayload>(job.Payload);
            var video = await videos.GetByIdAsync(request.VideoId, cancellationToken);
            if (video is not null)
            {
                var orders = await FindOrdersAsync(job.WorkflowNodeRunId, video.Id, request.SceneOrder, cancellationToken, request.SceneTargets);
                var quality = AutomaticVideoQualityPayload.Read(video.QaResult);
                foreach (var order in orders)
                    quality = quality.Complete(order, new VideoSceneEvaluationResponse("failed", time.GetUtcNow(), "video-quality-v1",
                        new Dictionary<string, object?>(), "Automatic evaluation failed after its retry limit."));
                video.QaResult = AutomaticVideoQualityPayload.Write(video.QaResult, quality);
                video.UpdatedAt = time.GetUtcNow().UtcDateTime;
            }
        }
        else
        {
            job.Status = "queued";
            job.Stage = "Queued";
            job.AvailableAt = time.GetUtcNow().AddSeconds(5 * Math.Max(1, job.Attempt)).UtcDateTime;
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<int>> FindOrdersAsync(Guid nodeId, Guid videoId, int? requestedScene,
        CancellationToken cancellationToken, IReadOnlyList<VideoQualitySceneJobTarget>? storedTargets = null)
    {
        var targets = storedTargets ?? await FindTargetsAsync(nodeId, videoId, requestedScene, cancellationToken);
        return targets.Where(target => requestedScene is null || target.SceneOrder == requestedScene.Value)
            .Select(target => target.SceneOrder).Distinct().ToArray();
    }

    private async Task<IReadOnlyList<VideoQualitySceneJobTarget>> FindTargetsAsync(Guid nodeId, Guid videoId, int? requestedScene,
        CancellationToken cancellationToken)
    {
        var render = (await jobs.FindAsync(candidate => candidate.WorkflowNodeRunId == nodeId && candidate.Kind == "render", cancellationToken))
            .Select(candidate => TryReadRender(candidate, videoId))
            .FirstOrDefault(candidate => candidate is not null);
        return render?.Payload.Storyboard.AiShots?
            .Where(shot => requestedScene is null || shot.SceneOrder == requestedScene.Value)
            .OrderBy(shot => shot.SceneOrder)
            .Select(shot =>
            {
                var state = render.Value.Payload.AiShotStates.FirstOrDefault(item => item.SceneOrder == shot.SceneOrder &&
                    item.Fingerprint == shot.Fingerprint && item.Status == "stored");
                return new VideoQualitySceneJobTarget(shot.SceneOrder, shot.MockupId, shot.Fingerprint,
                    state?.StorageKey, state?.StorageVersion, shot.ProviderKey, shot.SourceHash,
                    VideoOutputFormats.Resolve(render.Value.Payload.Storyboard.OutputFormat).AspectRatio, shot.SourceCrop);
            }).ToArray() ?? [];
    }

    private static bool Targets(string payload, Guid videoId, int? sceneOrder)
    {
        try
        {
            var request = WorkflowJson.Read<VideoQualityJobPayload>(payload);
            return request.VideoId == videoId && request.SceneOrder == sceneOrder;
        }
        catch (JsonException) { return false; }
    }

    private static (MediaJob Job, AiRenderJobPayload Payload)? TryReadRender(MediaJob job, Guid videoId)
    {
        try
        {
            var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
            return payload.VideoId == videoId ? (job, payload) : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record AutomaticVideoQualityPayload(IReadOnlyList<AutomaticSceneQualityState> Scenes,
    AutomaticQualitySummary? Summary = null)
{
    public static AutomaticVideoQualityPayload Read(string? qa)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(qa) ? "{}" : qa);
            return document.RootElement.TryGetProperty("videoQuality", out var value)
                ? value.Deserialize<AutomaticVideoQualityPayload>(WorkflowJson.Options) ?? new([])
                : new([]);
        }
        catch (JsonException) { return new([]); }
    }

    public static string Write(string? qa, AutomaticVideoQualityPayload payload)
    {
        var root = new Dictionary<string, JsonElement>();
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(qa) ? "{}" : qa);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject()) root[property.Name] = property.Value.Clone();
        }
        catch (JsonException) { }
        root["videoQuality"] = JsonSerializer.SerializeToElement(payload, WorkflowJson.Options);
        return JsonSerializer.Serialize(root, WorkflowJson.Options);
    }

    public AutomaticVideoQualityPayload Queue(IEnumerable<int> orders, DateTimeOffset now) =>
        Replace(orders, state => state with { Evaluation = new("queued", now, "video-quality-v1", new Dictionary<string, object?>()) }, "queued", now);

    public AutomaticVideoQualityPayload MarkRunning(IEnumerable<int> orders, DateTimeOffset now) =>
        Replace(orders, state => state with { Evaluation = new("running", now, "video-quality-v1", new Dictionary<string, object?>()) }, "running", now);

    public AutomaticVideoQualityPayload Complete(int order, VideoSceneEvaluationResponse evaluation) =>
        Replace([order], state => state with { Evaluation = evaluation }, AggregateStatus(evaluation.Status), evaluation.EvaluatedAtUtc ?? DateTimeOffset.UtcNow);

    private AutomaticVideoQualityPayload Replace(IEnumerable<int> orders, Func<AutomaticSceneQualityState, AutomaticSceneQualityState> update,
        string summaryStatus, DateTimeOffset now)
    {
        var ids = orders.Distinct().ToHashSet();
        var all = Scenes.Where(scene => !ids.Contains(scene.SceneOrder)).ToList();
        foreach (var order in ids)
        {
            var existing = Scenes.SingleOrDefault(scene => scene.SceneOrder == order) ?? new AutomaticSceneQualityState(order, null, []);
            all.Add(update(existing));
        }
        var ordered = all.OrderBy(scene => scene.SceneOrder).ToArray();
        var aggregate = ordered.Select(scene => scene.Evaluation?.Status).Where(status => status is not null).ToArray();
        var status = aggregate.Contains("failed") ? "failed" : aggregate.Contains("running") ? "running" :
            aggregate.Contains("queued") ? "queued" : aggregate.Length > 0 && aggregate.All(value => value is "completed" or "skipped") ? "completed" : summaryStatus;
        return new(ordered, new AutomaticQualitySummary(status, now));
    }

    private static string AggregateStatus(string status) => status is "failed" ? "failed" : status is "running" or "queued" ? status : "completed";
}

internal sealed record AutomaticSceneQualityState(int SceneOrder, VideoSceneEvaluationResponse? Evaluation,
    IReadOnlyList<JsonElement> Reviews);
internal sealed record AutomaticQualitySummary(string Status, DateTimeOffset UpdatedAtUtc);
