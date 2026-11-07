using APCS.Application.Abstractions.VideoQuality;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class VideoQualityJobServiceTests
{
    [TestMethod]
    public async Task ProcessNextAsync_MissingClip_CompletesJobAndRecordsSkippedScene()
    {
        var fixture = new WorkflowTestData();
        var runId = Guid.NewGuid();
        var video = new PromoVideo { Id = Guid.NewGuid(), WorkflowRunId = runId, QaResult = "{\"existing\":true}" };
        fixture.VideoRows.Add(video);
        var target = new VideoQualitySceneJobTarget(2, Guid.NewGuid(), "fingerprint", null, null);
        var job = new MediaJob { Id = Guid.NewGuid(), WorkflowRunId = runId, WorkflowNodeRunId = Guid.NewGuid(),
            Status = "leased", LeaseToken = Guid.NewGuid(), Payload = WorkflowJson.Write(new VideoQualityJobPayload(video.Id, SceneTargets: [target])) };
        fixture.State.Setup(x => x.ClaimAsync(It.IsAny<DateTime>(), It.IsAny<Guid>(), "video_quality", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var runner = new Mock<IVideoQualityEvidenceRunner>(MockBehavior.Strict);
        var service = new VideoQualityJobService(fixture.Runs.Object, fixture.Nodes.Object, fixture.Jobs.Object, fixture.Videos.Object,
            fixture.Assets.Object, fixture.State.Object, fixture.Unit.Object, fixture.Storage.Object, runner.Object, fixture.Time);

        var processed = await service.ProcessNextAsync(default);

        processed.Should().BeTrue();
        job.Status.Should().Be("succeeded");
        job.Progress.Should().Be(100);
        job.LeaseToken.Should().BeNull();
        video.QaResult.Should().Contain("\"existing\":true").And.Contain("skipped").And.Contain("does not have a stored clip");
        runner.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ProcessNextAsync_DeletedVideo_CompletesJobWithoutRunningEvaluator()
    {
        var fixture = new WorkflowTestData();
        var job = new MediaJob { Id = Guid.NewGuid(), WorkflowRunId = Guid.NewGuid(), WorkflowNodeRunId = Guid.NewGuid(),
            Status = "leased", LeaseToken = Guid.NewGuid(), Payload = WorkflowJson.Write(new VideoQualityJobPayload(Guid.NewGuid())) };
        fixture.State.Setup(x => x.ClaimAsync(It.IsAny<DateTime>(), It.IsAny<Guid>(), "video_quality", It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var runner = new Mock<IVideoQualityEvidenceRunner>(MockBehavior.Strict);
        var service = new VideoQualityJobService(fixture.Runs.Object, fixture.Nodes.Object, fixture.Jobs.Object, fixture.Videos.Object,
            fixture.Assets.Object, fixture.State.Object, fixture.Unit.Object, fixture.Storage.Object, runner.Object, fixture.Time);

        var processed = await service.ProcessNextAsync(default);

        processed.Should().BeTrue();
        job.Status.Should().Be("succeeded");
        job.LeaseToken.Should().BeNull();
        runner.VerifyNoOtherCalls();
    }
}
