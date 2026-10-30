using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Abstractions.VideoQuality;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoQualityReviewServiceTests
{
    [TestMethod]
    public async Task SaveReviewAsync_IssuePresentInSource_SuggestsSourceImageAndDoesNotCreateLesson()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, _, _) = CreateService(fixture, "lifestyle_in_use");

        var result = await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(1000, 2300, "hands", "medium", true, "The hand shape is already wrong in the source image.",
                ConfirmSuggestion: true, CandidateLesson: "This must not become a lesson."), default);

        result.IsSuccess.Should().BeTrue();
        var observation = result.Value.Reviews.Should().ContainSingle().Subject;
        observation.Suggestion.Target.Should().Be("source_image");
        observation.Suggestion.Confidence.Should().Be("high");
        observation.ConfirmedTarget.Should().Be("source_image");
        observation.LessonId.Should().BeNull();
        fixture.Unit.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SaveReviewAsync_ConfirmedLifestyleHandIssue_CreatesCandidateLessonAndLinksItToReview()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, rag, _) = CreateService(fixture, "lifestyle_in_use");
        var lessonId = Guid.NewGuid();
        rag.Setup(service => service.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(lessonId);

        var result = await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(2300, 2800, "hands", "high", false, "The hand jerks while touching the collar.",
                ConfirmSuggestion: true, CandidateLesson: "For a collar touch, keep one slow hand path and preserve finger anatomy."), default);

        result.IsSuccess.Should().BeTrue();
        var observation = result.Value.Reviews.Should().ContainSingle().Subject;
        observation.Suggestion.Target.Should().Be("prompt_template");
        observation.ConfirmedTarget.Should().Be("prompt_template");
        observation.LessonId.Should().Be(lessonId);
        rag.Verify(service => service.IngestAsync(It.Is<RagIngestRequest>(request =>
            request.ContentType == RagContentTypes.Video && request.SourceType == "video-experience" &&
            request.Metadata != null && request.Metadata.ContainsKey("experienceStatus") && request.Metadata.ContainsKey("ownerId") &&
            Equals(request.Metadata["experienceStatus"], "candidate") &&
            Equals(request.Metadata["ownerId"], WorkflowTestData.Owner.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SaveReviewAsync_AppliedLessonOnlySuggestsRagWhenSellerMarksItSuspect()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, _, _) = CreateService(fixture, "lifestyle_in_use", ragApplied: true);

        var ordinary = await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(0, 400, "hands", "low", false, "A brief hand twitch.", ConfirmSuggestion: false), default);
        var suspected = await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(500, 900, "hands", "low", false, "The new instruction appears to conflict with the action.",
                ConfirmSuggestion: false, SuspectRagExperience: true), default);

        ordinary.Value.Reviews.Should().ContainSingle().Which.Suggestion.Target.Should().Be("prompt_template");
        suspected.Value.Reviews.Last().Suggestion.Target.Should().Be("rag_experience");
    }

    [TestMethod]
    public async Task EvaluateAsync_NoStoredSceneClip_ReturnsSkippedEvidenceWithoutBlockingReview()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, _, _) = CreateService(fixture, "lifestyle_in_use");

        var result = await service.EvaluateAsync(run.Id, video.Id, 0, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Evaluation.Should().NotBeNull();
        result.Value.Evaluation!.Status.Should().Be("skipped");
        result.Value.Reviews.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_ForeignRun_ReturnsNotFoundInsteadOfExposingAnotherSellersReview()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, _, _) = CreateService(fixture, "lifestyle_in_use");
        run.UserId = Guid.NewGuid();

        var result = await service.GetAsync(run.Id, video.Id, 0, default);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VideoSceneNotFound");
        fixture.Unit.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task EvaluateAsync_LocalEvidenceFailure_PersistsFailedStatusWithoutLosingTheScene()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, _, evidence) = CreateService(fixture, "lifestyle_in_use");
        var plan = WorkflowJson.Read<ScenePlan>(fixture.SceneRows.Single().SceneConfig);
        var job = fixture.AddJob(run);
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(video.Id,
            new Storyboard("ai_shot", 1, 4, "tshirt", [plan], "board-fingerprint"),
            [new AiShotJobState(0, "scene-fingerprint", StorageKey: "private/scene.mp4", StorageVersion: "1", Status: "stored")]));
        fixture.Storage.Setup(storage => storage.SignRead("private/scene.mp4", "video", "1", false)).Returns("https://local/scene.mp4");
        fixture.Storage.Setup(storage => storage.ReadImageAsync("private/mockup", "1", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        evidence.Setup(runner => runner.EvaluateAsync(It.IsAny<VideoQualityEvidenceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("PaddleOCR is unavailable."));

        var result = await service.EvaluateAsync(run.Id, video.Id, 0, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Evaluation!.Status.Should().Be("failed");
        result.Value.Evaluation.Detail.Should().Contain("PaddleOCR is unavailable");
        fixture.VideoRows.Single().QaResult.Should().Contain("failed");
    }

    [TestMethod]
    public async Task ReviseLessonAsync_CreatesANewCandidateRevisionAndRelinksTheReview()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, rag, _) = CreateService(fixture, "lifestyle_in_use");
        var originalLessonId = Guid.NewGuid();
        var revisedLessonId = Guid.NewGuid();
        rag.Setup(service => service.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalLessonId);
        var saved = await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(100, 800, "hands", "medium", false, "Hand movement is abrupt.", ConfirmSuggestion: true,
                CandidateLesson: "Keep one slow hand path."), default);
        var review = saved.Value.Reviews.Single();
        rag.Setup(service => service.ReviseVideoExperienceAsync(originalLessonId, WorkflowTestData.Owner,
                "Keep one deliberate hand path and preserve finger anatomy.", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagVideoExperienceDto(revisedLessonId, "candidate", "2",
                "Keep one deliberate hand path and preserve finger anatomy."));

        var result = await service.ReviseLessonAsync(run.Id, video.Id, 0, originalLessonId,
            new(review.Id, "Keep one deliberate hand path and preserve finger anatomy."), default);

        result.IsSuccess.Should().BeTrue();
        var revisedReview = result.Value.Reviews.Single();
        revisedReview.LessonId.Should().Be(revisedLessonId);
        revisedReview.LessonRevision.Should().Be("2");
        revisedReview.LessonContent.Should().Be("Keep one deliberate hand path and preserve finger anatomy.");
        rag.Verify(service => service.ReviseVideoExperienceAsync(originalLessonId, WorkflowTestData.Owner,
            "Keep one deliberate hand path and preserve finger anatomy.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GetAsync_ReadsTheCurrentPrivateLessonStatusAfterThePageIsReloaded()
    {
        var fixture = new WorkflowTestData();
        var (service, run, video, rag, _) = CreateService(fixture, "lifestyle_in_use");
        var lessonId = Guid.NewGuid();
        rag.Setup(service => service.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(lessonId);
        await service.SaveReviewAsync(run.Id, video.Id, 0,
            new(100, 800, "hands", "medium", false, "Hand movement is abrupt.", ConfirmSuggestion: true,
                CandidateLesson: "Keep one slow hand path."), default);
        rag.Setup(service => service.GetVideoExperienceAsync(lessonId, WorkflowTestData.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagVideoExperienceDto(lessonId, "verified", "1", "Keep one slow hand path."));

        var result = await service.GetAsync(run.Id, video.Id, 0, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reviews.Single().LessonStatus.Should().Be("verified");
    }

    private static (VideoQualityReviewService Service, WorkflowRun Run, PromoVideo Video, Mock<IRagService> Rag,
        Mock<IVideoQualityEvidenceRunner> Evidence) CreateService(
        WorkflowTestData fixture, string templateCode, bool ragApplied = false)
    {
        var product = fixture.AddProduct();
        var source = fixture.AddAsset(product, "Lifestyle");
        var run = fixture.AddRun(product);
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 4,
            AiScenes: [new(MockupId: source.Id, TemplateCode: templateCode, RightsConfirmed: true)]);
        var plan = new VideoStoryboardPlanner().Plan(product, [source], config).Value.Scenes.Single();
        if (ragApplied)
            plan = plan with { AiPromptTrace = plan.AiPromptTrace! with { RagStatus = "applied" } };
        var video = new PromoVideo { Id = Guid.NewGuid(), ProductId = product.Id, WorkflowRunId = run.Id, QaResult = "{\"fullDecode\":true}",
            Mode = "ai_shot", Status = "completed", ApprovalStatus = "pending", VideoResolution = "1080x1920", FileFormat = "mp4",
            AspectRatio = "9:16", PlatformTarget = "etsy", ConfigSnapshot = "{}" };
        fixture.VideoRows.Add(video);
        fixture.SceneRows.Add(new PromoVideoScene { Id = Guid.NewGuid(), PromoVideoId = video.Id, MockupImageId = source.Id,
            SceneOrder = 1, DurationSeconds = 4, GenerationStrategy = "ai_shot", SceneConfig = WorkflowJson.Write(plan), Warnings = "[]" });
        var current = new Mock<ICurrentUser>();
        current.SetupGet(item => item.UserId).Returns(WorkflowTestData.Owner);
        var rag = new Mock<IRagService>();
        rag.Setup(service => service.GetVideoExperienceAsync(It.IsAny<Guid>(), WorkflowTestData.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagVideoExperienceDto?)null);
        var evidence = new Mock<IVideoQualityEvidenceRunner>();
        var service = new VideoQualityReviewService(current.Object, fixture.Runs.Object, fixture.Videos.Object,
            fixture.Scenes.Object, fixture.Assets.Object, fixture.Nodes.Object, fixture.Jobs.Object, fixture.Storage.Object,
            evidence.Object, rag.Object, fixture.Unit.Object, fixture.Time);
        return (service, run, video, rag, evidence);
    }
}
