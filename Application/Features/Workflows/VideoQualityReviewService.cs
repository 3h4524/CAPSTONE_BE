using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Stores seller review observations, optional offline evidence and private RAG lesson candidates.</summary>
public interface IVideoQualityReviewService
{
    Task<Result<VideoSceneQualityResponse>> GetAsync(Guid runId, Guid videoId, int sceneOrder, CancellationToken cancellationToken);
    Task<Result<VideoSceneQualityResponse>> EvaluateAsync(Guid runId, Guid videoId, int sceneOrder, CancellationToken cancellationToken);
    Task<Result<VideoSceneQualityResponse>> SaveReviewAsync(Guid runId, Guid videoId, int sceneOrder,
        SaveVideoSceneReviewRequest request, CancellationToken cancellationToken);
    Task<Result<VideoExperienceResponse>> SetLessonStatusAsync(Guid lessonId, SetVideoExperienceStatusRequest request,
        CancellationToken cancellationToken);
    Task<Result<VideoSceneQualityResponse>> ReviseLessonAsync(Guid runId, Guid videoId, int sceneOrder, Guid lessonId,
        ReviseVideoExperienceRequest request, CancellationToken cancellationToken);
}

public sealed class VideoQualityReviewService(ICurrentUser user, IRepository<WorkflowRun> runs,
    IRepository<PromoVideo> videos, IRepository<PromoVideoScene> scenes, IRepository<MockupImage> assets,
    IRepository<WorkflowNodeRun> nodes, IRepository<MediaJob> jobs, IMediaStorage storage,
    IVideoQualityEvidenceRunner evidenceRunner, IRagService rag, IUnitOfWork unitOfWork, TimeProvider time)
    : IVideoQualityReviewService
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
        { "body", "hands", "person_product", "artwork", "text", "camera", "other" };
    private static readonly HashSet<string> Severities = new(StringComparer.Ordinal)
        { "low", "medium", "high", "critical" };
    private static readonly HashSet<string> Targets = new(StringComparer.Ordinal)
        { "source_image", "prompt_template", "prompt_rule", "rag_experience", "unknown" };

    public async Task<Result<VideoSceneQualityResponse>> GetAsync(Guid runId, Guid videoId, int sceneOrder,
        CancellationToken cancellationToken)
    {
        var context = await FindSceneAsync(runId, videoId, sceneOrder, cancellationToken);
        return context is null
            ? Result.Failure<VideoSceneQualityResponse>(Missing())
            : Result.Success(await MapAsync(context.Video.Id, sceneOrder, ReadPayload(context.Video.QaResult), cancellationToken));
    }

    public async Task<Result<VideoSceneQualityResponse>> EvaluateAsync(Guid runId, Guid videoId, int sceneOrder,
        CancellationToken cancellationToken)
    {
        var context = await FindSceneAsync(runId, videoId, sceneOrder, cancellationToken);
        if (context is null) return Result.Failure<VideoSceneQualityResponse>(Missing());
        var payload = ReadPayload(context.Video.QaResult);
        var clip = await FindStoredClipAsync(context.Run, context.Video.Id, sceneOrder, cancellationToken);
        VideoQualityEvidenceResult evidence;
        if (clip is null || context.Source?.StorageKey is null)
        {
            evidence = new("skipped", "video-quality-v1", new Dictionary<string, object?>(),
                "This scene does not have a stored AI clip and approved source image.");
        }
        else
        {
            var state = payload.Find(sceneOrder) ?? new SceneQualityState(sceneOrder, null, []);
            payload = payload.Replace(state with
            {
                Evaluation = new("queued", time.GetUtcNow(), "video-quality-v1", new Dictionary<string, object?>())
            });
            await SavePayloadAsync(context.Video, payload, cancellationToken);
            state = payload.Find(sceneOrder)!;
            payload = payload.Replace(state with
            {
                Evaluation = new("running", time.GetUtcNow(), "video-quality-v1", new Dictionary<string, object?>())
            });
            await SavePayloadAsync(context.Video, payload, cancellationToken);
            try
            {
                var sourceBytes = await storage.ReadImageAsync(context.Source.StorageKey, context.Source.StorageVersion, cancellationToken);
                evidence = await evidenceRunner.EvaluateAsync(new(clip.Url, sourceBytes, sceneOrder), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                evidence = new("failed", "video-quality-v1", new Dictionary<string, object?>(),
                    $"Local quality evaluation failed: {exception.Message}");
            }
        }

        var completedState = payload.Find(sceneOrder) ?? new SceneQualityState(sceneOrder, null, []);
        payload = payload.Replace(completedState with
        {
            Evaluation = new(evidence.Status, time.GetUtcNow(), evidence.EvaluatorVersion, evidence.Metrics, evidence.Detail)
        });
        await SavePayloadAsync(context.Video, payload, cancellationToken);
        return Result.Success(await MapAsync(context.Video.Id, sceneOrder, payload, cancellationToken));
    }

    public async Task<Result<VideoSceneQualityResponse>> SaveReviewAsync(Guid runId, Guid videoId, int sceneOrder,
        SaveVideoSceneReviewRequest request, CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return Result.Failure<VideoSceneQualityResponse>(validation);
        var context = await FindSceneAsync(runId, videoId, sceneOrder, cancellationToken);
        if (context is null) return Result.Failure<VideoSceneQualityResponse>(Missing());
        var payload = ReadPayload(context.Video.QaResult);
        var state = payload.Find(sceneOrder) ?? new SceneQualityState(sceneOrder, null, []);
        var suggestion = Suggest(request, context.Plan);
        var target = request.TargetOverride is null ? suggestion.Target : request.TargetOverride.Trim().ToLowerInvariant();
        var confirmed = request.ConfirmSuggestion || request.TargetOverride is not null;
        var now = time.GetUtcNow();
        var existing = request.ReviewId is Guid reviewId ? state.Reviews.SingleOrDefault(item => item.Id == reviewId) : null;
        if (request.ReviewId is not null && existing is null)
            return Result.Failure<VideoSceneQualityResponse>(Error.NotFound("VideoReviewNotFound", "Video review was not found."));

        var review = new SceneReviewState(existing?.Id ?? Guid.NewGuid(), request.StartMilliseconds, request.EndMilliseconds,
            request.Category.Trim().ToLowerInvariant(), request.Severity.Trim().ToLowerInvariant(), request.PresentInSourceImage,
            request.Description.Trim(), confirmed ? "confirmed" : "suggested", suggestion, confirmed ? target : null,
            existing?.LessonId, existing?.LessonStatus, existing?.LessonRevision, existing?.LessonContent,
            existing?.CreatedAtUtc ?? now, now);
        if (!string.IsNullOrWhiteSpace(request.CandidateLesson) && confirmed && target is not ("source_image" or "unknown"))
        {
            var content = NormalizeLesson(request.CandidateLesson);
            var lesson = await CreateCandidateLessonAsync(context, review, content, cancellationToken);
            review = review with { LessonId = lesson, LessonStatus = RagExperienceStatuses.Candidate,
                LessonRevision = "1", LessonContent = content };
        }
        var reviews = state.Reviews.Where(item => item.Id != review.Id).Append(review)
            .OrderBy(item => item.StartMilliseconds).ThenBy(item => item.CreatedAtUtc).ToArray();
        payload = payload.Replace(state with { Reviews = reviews });
        await SavePayloadAsync(context.Video, payload, cancellationToken);
        return Result.Success(await MapAsync(context.Video.Id, sceneOrder, payload, cancellationToken));
    }

    public async Task<Result<VideoExperienceResponse>> SetLessonStatusAsync(Guid lessonId,
        SetVideoExperienceStatusRequest request, CancellationToken cancellationToken)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(status) || !RagExperienceStatuses.IsSupported(status))
            return Result.Failure<VideoExperienceResponse>(Error.Validation("Choose a supported lesson status."));
        var owner = user.UserId;
        if (owner is null) return Result.Failure<VideoExperienceResponse>(Missing());
        var lesson = await rag.SetVideoExperienceStatusAsync(lessonId, owner.Value, status, cancellationToken);
        return lesson is null
            ? Result.Failure<VideoExperienceResponse>(Error.NotFound("VideoExperienceNotFound", "Video lesson was not found or is not ready."))
            : Result.Success<VideoExperienceResponse>(new(lesson.DocumentId, lesson.Status, lesson.Version, lesson.Content));
    }

    public async Task<Result<VideoSceneQualityResponse>> ReviseLessonAsync(Guid runId, Guid videoId, int sceneOrder,
        Guid lessonId, ReviseVideoExperienceRequest request, CancellationToken cancellationToken)
    {
        if (request.ReviewId == Guid.Empty || string.IsNullOrWhiteSpace(request.Content)
            || NormalizeLesson(request.Content).Length > 1500)
            return Result.Failure<VideoSceneQualityResponse>(Error.Validation("A revised lesson must contain 1-1500 characters."));
        var context = await FindSceneAsync(runId, videoId, sceneOrder, cancellationToken);
        if (context is null) return Result.Failure<VideoSceneQualityResponse>(Missing());
        var payload = ReadPayload(context.Video.QaResult);
        var state = payload.Find(sceneOrder);
        var review = state?.Reviews.SingleOrDefault(item => item.Id == request.ReviewId && item.LessonId == lessonId);
        if (state is null || review is null || user.UserId is not Guid ownerId)
            return Result.Failure<VideoSceneQualityResponse>(Error.NotFound("VideoExperienceNotFound", "Video lesson review was not found."));

        var revised = await rag.ReviseVideoExperienceAsync(lessonId, ownerId, NormalizeLesson(request.Content), cancellationToken);
        if (revised is null)
            return Result.Failure<VideoSceneQualityResponse>(Error.NotFound("VideoExperienceNotFound", "Video lesson was not found."));
        var revisedReview = review with { LessonId = revised.DocumentId, LessonStatus = revised.Status,
            LessonRevision = revised.Version, LessonContent = revised.Content, UpdatedAtUtc = time.GetUtcNow() };
        payload = payload.Replace(state with
        {
            Reviews = state.Reviews.Select(item => item.Id == review.Id ? revisedReview : item).ToArray()
        });
        await SavePayloadAsync(context.Video, payload, cancellationToken);
        return Result.Success(await MapAsync(context.Video.Id, sceneOrder, payload, cancellationToken));
    }

    private async Task<Guid> CreateCandidateLessonAsync(SceneContext context, SceneReviewState review, string candidate,
        CancellationToken cancellationToken)
    {
        var content = NormalizeLesson(candidate);
        if (content.Length is < 1 or > 1500)
            throw new ArgumentException("Candidate lesson must contain 1–1500 characters.", nameof(candidate));
        var lessonId = Guid.NewGuid();
        var metadata = new Dictionary<string, object?>
        {
            ["ownerId"] = user.UserId!.Value.ToString(),
            ["experienceStatus"] = RagExperienceStatuses.Candidate,
            ["templateCode"] = context.Plan.AiTemplateCode ?? string.Empty,
            ["modelKey"] = context.Plan.AiModelKey ?? AiVideoModel.ModelKey,
            ["primaryAction"] = context.Plan.AiSettings?.PrimaryAction ?? string.Empty,
            ["videoId"] = context.Video.Id.ToString(),
            ["sceneOrder"] = context.SceneOrder,
            ["reviewId"] = review.Id.ToString()
        };
        return await rag.IngestAsync(new RagIngestRequest("Verified-video lesson candidate", "video-experience", null,
            "1", "en", content, metadata, RagContentTypes.Video,
            $"video-experience:{user.UserId!.Value}:{lessonId}"), cancellationToken);
    }

    private async Task<SceneContext?> FindSceneAsync(Guid runId, Guid videoId, int sceneOrder, CancellationToken cancellationToken)
    {
        if (sceneOrder < 0 || user.UserId is not Guid ownerId) return null;
        var run = await runs.GetByIdAsync(runId, cancellationToken);
        if (run?.UserId != ownerId) return null;
        var video = await videos.GetByIdAsync(videoId, cancellationToken);
        if (video?.WorkflowRunId != runId) return null;
        var scene = (await scenes.FindAsync(item => item.PromoVideoId == videoId && item.SceneOrder == sceneOrder + 1,
            cancellationToken)).SingleOrDefault();
        if (scene is null) return null;
        try
        {
            var plan = WorkflowJson.Read<ScenePlan>(scene.SceneConfig);
            var source = scene.MockupImageId is Guid sourceId ? await assets.GetByIdAsync(sourceId, cancellationToken) : null;
            return new(run, video, plan, source, sceneOrder);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<StoredSceneClip?> FindStoredClipAsync(WorkflowRun run, Guid videoId, int sceneOrder,
        CancellationToken cancellationToken)
    {
        var generate = (await nodes.FindAsync(item => item.WorkflowRunId == run.Id && item.NodeType == "generate-video",
            cancellationToken)).SingleOrDefault();
        if (generate is null) return null;
        foreach (var job in await jobs.FindAsync(item => item.WorkflowNodeRunId == generate.Id && item.Kind == "render", cancellationToken))
        {
            try
            {
                var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
                if (payload.VideoId != videoId) continue;
                var state = payload.AiShotStates.SingleOrDefault(item => item.SceneOrder == sceneOrder && item.Status == "stored"
                    && item.StorageKey is not null && item.StorageVersion is not null);
                if (state is not null)
                    return new(storage.SignRead(state.StorageKey!, "video", state.StorageVersion), state.StorageKey!, state.StorageVersion!);
            }
            catch (JsonException)
            {
                // Standard render payloads do not contain individual AI scene clips.
            }
        }
        return null;
    }

    private async Task SavePayloadAsync(PromoVideo video, VideoQualityPayload payload, CancellationToken cancellationToken)
    {
        video.QaResult = WritePayload(video.QaResult, payload);
        video.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await videos.UpdateAsync(video, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static Error? Validate(SaveVideoSceneReviewRequest request)
    {
        if (request.StartMilliseconds < 0 || request.EndMilliseconds < request.StartMilliseconds || request.EndMilliseconds > 30_000)
            return Error.Validation("Review timestamps must be within one 30-second video and in order.");
        if (!Categories.Contains(request.Category?.Trim().ToLowerInvariant() ?? string.Empty)
            || !Severities.Contains(request.Severity?.Trim().ToLowerInvariant() ?? string.Empty))
            return Error.Validation("Choose a supported review category and severity.");
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 1000)
            return Error.Validation("Describe the issue using 1–1000 characters.");
        if (request.TargetOverride is not null && !Targets.Contains(request.TargetOverride.Trim().ToLowerInvariant()))
            return Error.Validation("Choose a supported correction target.");
        if (request.CandidateLesson is { Length: > 1500 })
            return Error.Validation("Candidate lesson must be at most 1500 characters.");
        return null;
    }

    private static CorrectionSuggestionResponse Suggest(SaveVideoSceneReviewRequest review, ScenePlan plan)
    {
        if (review.PresentInSourceImage)
            return new("source_image", "The issue is already visible in the approved source image.", "high");
        if (review.SuspectRagExperience && string.Equals(plan.AiPromptTrace?.RagStatus, "applied", StringComparison.Ordinal))
            return new("rag_experience", "A retrieved lesson was included in this scene prompt; check it for conflicting direction.", "medium");
        var category = review.Category.Trim().ToLowerInvariant();
        if (category is "artwork" or "text" or "body" or "person_product")
            return new("prompt_rule", "This issue is covered by the shared product-preservation or continuity rules.", "medium");
        if (category is "hands" or "camera" && plan.AiTemplateCode == "lifestyle_in_use")
            return new("prompt_template", "The observed action or framing belongs to the Lifestyle In-Use template.", "medium");
        return new("unknown", "One clip does not provide enough evidence to attribute a correction target.", "low");
    }

    private async Task<VideoSceneQualityResponse> MapAsync(Guid videoId, int sceneOrder, VideoQualityPayload payload,
        CancellationToken cancellationToken)
    {
        var state = payload.Find(sceneOrder);
        var ownerId = user.UserId;
        if (state is null) return new(videoId, sceneOrder, null, []);
        var reviews = new List<VideoSceneReviewResponse>(state.Reviews.Count);
        foreach (var review in state.Reviews)
        {
            RagVideoExperienceDto? lesson = null;
            if (review.LessonId is Guid lessonId && ownerId is Guid owner)
            {
                try
                {
                    lesson = await rag.GetVideoExperienceAsync(lessonId, owner, cancellationToken);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    // Preserve a saved review when the optional lesson-status lookup is temporarily unavailable.
                }
            }
            reviews.Add(MapReview(review, lesson?.Status));
        }
        return new(videoId, sceneOrder, state.Evaluation, reviews);
    }

    private static VideoSceneReviewResponse MapReview(SceneReviewState review, string? retrievedLessonStatus) => new(review.Id, review.StartMilliseconds,
        review.EndMilliseconds, review.Category, review.Severity, review.PresentInSourceImage, review.Description,
        review.Status, review.Suggestion, review.ConfirmedTarget, review.LessonId, retrievedLessonStatus ?? review.LessonStatus,
        review.LessonRevision, review.LessonContent,
        review.CreatedAtUtc, review.UpdatedAtUtc);

    private static VideoQualityPayload ReadPayload(string? qa)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(qa) ? "{}" : qa);
            return document.RootElement.TryGetProperty("videoQuality", out var value)
                ? value.Deserialize<VideoQualityPayload>(WorkflowJson.Options) ?? new([])
                : new([]);
        }
        catch (JsonException)
        {
            return new([]);
        }
    }

    private static string WritePayload(string? qa, VideoQualityPayload payload)
    {
        var root = new Dictionary<string, JsonElement>();
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(qa) ? "{}" : qa);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject()) root[property.Name] = property.Value.Clone();
        }
        catch (JsonException)
        {
            // A malformed legacy QA value is replaced with the new structured quality payload.
        }
        root["videoQuality"] = JsonSerializer.SerializeToElement(payload, WorkflowJson.Options);
        return JsonSerializer.Serialize(root, WorkflowJson.Options);
    }

    private static string NormalizeLesson(string value) => string.Join(' ', value.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static Error Missing() => Error.NotFound("VideoSceneNotFound", "Video scene was not found.");

    private sealed record SceneContext(WorkflowRun Run, PromoVideo Video, ScenePlan Plan, MockupImage? Source, int SceneOrder);
    private sealed record StoredSceneClip(string Url, string StorageKey, string StorageVersion);
    private sealed record VideoQualityPayload(IReadOnlyList<SceneQualityState> Scenes)
    {
        public SceneQualityState? Find(int sceneOrder) => Scenes.SingleOrDefault(item => item.SceneOrder == sceneOrder);
        public VideoQualityPayload Replace(SceneQualityState state) => new(Scenes.Where(item => item.SceneOrder != state.SceneOrder)
            .Append(state).OrderBy(item => item.SceneOrder).ToArray());
    }
    private sealed record SceneQualityState(int SceneOrder, VideoSceneEvaluationResponse? Evaluation,
        IReadOnlyList<SceneReviewState> Reviews);
    private sealed record SceneReviewState(Guid Id, int StartMilliseconds, int EndMilliseconds, string Category,
        string Severity, bool PresentInSourceImage, string Description, string Status, CorrectionSuggestionResponse Suggestion,
        string? ConfirmedTarget, Guid? LessonId, string? LessonStatus, string? LessonRevision, string? LessonContent,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
}
