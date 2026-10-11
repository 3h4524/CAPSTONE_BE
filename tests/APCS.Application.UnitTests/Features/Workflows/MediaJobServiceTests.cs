using APCS.Application.Features.Workflows;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class MediaJobServiceTests
{
    [TestMethod]
    public async Task FailAsync_WrongLeaseToken_DoesNotCancelActiveWorker()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single(); var lease = job.LeaseToken;
        var result = await fixture.MediaService.FailAsync(job.Id, new(Guid.NewGuid(), "unexpected failure"), default);
        result.Error.Code.Should().Be("LeaseLost");
        job.Status.Should().Be("leased");
        job.LeaseToken.Should().Be(lease);
        fixture.RunRows.Single().Status.Should().Be("running");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ClaimAsync_ApprovedExport_ReturnsOnlyApprovedVideoThumbnailAndSourceMockup()
    {
        var fixture = await ExportFixtureAsync();
        var job = fixture.JobRows.Single();
        SetupClaim(fixture);
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false)).Returns("https://example.invalid/expiring");
        fixture.Storage.Setup(x => x.CreateUploadGrant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string type) => new UploadGrant("https://example.invalid/upload", new Dictionary<string, string>(), key, type));
        var result = await fixture.MediaService.ClaimAsync(default);
        result.Value!.Assets.Select(x => x.FileName).Should().BeEquivalentTo($"mockups/{fixture.AssetRows.Single().Id}.png", "video.mp4", "thumbnail.png");
        result.Value.Uploads.Keys.Should().Equal("zip");
        result.Value.Uploads["zip"].StorageKey.Should().Be(WorkflowTestData.OutputKey(job, "package.zip"));
        result.Value.Uploads["zip"].ResourceType.Should().Be("raw");
        result.Value.Payload.GetProperty("manifest").GetRawText().Should().NotContain("https:").And.NotContain("prompt");
    }

    [TestMethod]
    [DataRow("video_rejected")]
    [DataRow("source_rejected")]
    public async Task ClaimAsync_ExportApprovalRevoked_DoesNotGrantZipUpload(string revoked)
    {
        var fixture = await ExportFixtureAsync();
        SetupClaim(fixture);
        if (revoked == "video_rejected") fixture.VideoRows.Single().ApprovalStatus = "rejected";
        else fixture.AssetRows.Single().ApprovalStatus = "rejected";
        var result = await fixture.MediaService.ClaimAsync(default);
        result.Value.Should().BeNull();
        fixture.JobRows.Single().Status.Should().Be("queued");
        fixture.Storage.Verify(x => x.CreateUploadGrant(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteAsync_ApprovedExport_MarksOnlyExactPackageReadyAndCompletesRun()
    {
        var fixture = await ExportFixtureAsync();
        var job = fixture.JobRows.Single();
        SetupVerification(fixture);
        var completion = new JobCompletion(job.LeaseToken!.Value, WorkflowTestData.OutputKey(job, "package.zip"), "9", null, null, null);
        var result = await fixture.MediaService.CompleteAsync(job.Id, completion, default);
        result.IsSuccess.Should().BeTrue();
        fixture.ExportRows.Single().Status.Should().Be("ready");
        fixture.ExportRows.Single().StorageKey.Should().Be(completion.StorageKey);
        fixture.ItemRows.Single().ItemStatus.Should().Be("completed");
        fixture.RunRows.Single().Status.Should().Be("completed");
        fixture.RunRows.Single().CompletedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        job.Status.Should().Be("succeeded");
        job.LeaseToken.Should().BeNull();
        fixture.Storage.Verify(x => x.VerifyAsync(completion.StorageKey, "raw", "9", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("video_rejected", "NotApproved")]
    [DataRow("source_rejected", "SourceChanged")]
    [DataRow("source_revision", "SourceChanged")]
    public async Task CompleteAsync_ExportSourceOrApprovalChanged_DoesNotPublishZip(string reason, string error)
    {
        var fixture = await ExportFixtureAsync();
        var job = fixture.JobRows.Single();
        if (reason == "video_rejected") fixture.VideoRows.Single().ApprovalStatus = "rejected";
        if (reason == "source_rejected") fixture.AssetRows.Single().ApprovalStatus = "rejected";
        if (reason == "source_revision") fixture.AssetRows.Single().MetadataRevision++;
        SetupVerification(fixture);
        var result = await fixture.MediaService.CompleteAsync(job.Id, new(job.LeaseToken!.Value, WorkflowTestData.OutputKey(job, "package.zip"), "9", null, null, null), default);
        result.Error.Code.Should().Be(error);
        fixture.ExportRows.Single().Status.Should().Be("preparing");
        fixture.ItemRows.Single().ItemStatus.Should().Be("pending");
        fixture.RunRows.Single().Status.Should().Be("running");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("codec")]
    [DataRow("pixel_format")]
    [DataRow("width")]
    [DataRow("height")]
    [DataRow("fps")]
    [DataRow("duration")]
    [DataRow("empty")]
    [DataRow("size_limit")]
    [DataRow("audio")]
    [DataRow("decode")]
    public void ValidQa_InvalidTechnicalProperty_RejectsVideo(string invalid)
    {
        var qa = WorkflowTestData.Qa();
        qa = invalid switch
        {
            "codec" => qa with { Codec = "vp9" },
            "pixel_format" => qa with { PixelFormat = "yuv444p" },
            "width" => qa with { Width = 1079 },
            "height" => qa with { Height = 2159 },
            "fps" => qa with { Fps = 29.98 },
            "duration" => qa with { DurationSeconds = 12.05 },
            "empty" => qa with { Bytes = 0 },
            "size_limit" => qa with { Bytes = 100 * 1024 * 1024 },
            "audio" => qa with { HasAudio = true },
            "decode" => qa with { FullDecode = false },
            _ => throw new ArgumentException(invalid)
        };
        MediaJobService.ValidQa(qa, 12).Should().BeFalse();
    }

    [TestMethod]
    public void ValidQa_ValidBoundaryFrameToleranceAndSize_AcceptsOnlyInRange()
    {
        MediaJobService.ValidQa(WorkflowTestData.Qa(), 12).Should().BeTrue();
        MediaJobService.ValidQa(WorkflowTestData.Qa() with { DurationSeconds = 12.04, Bytes = 100 * 1024 * 1024 - 1 }, 12).Should().BeTrue();
        MediaJobService.ValidQa(WorkflowTestData.Qa() with { DurationSeconds = 12.044 }, 12).Should().BeFalse();
        MediaJobService.ValidQa(null, 12).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("at_expiry")]
    [DataRow("wrong_token")]
    [DataRow("cancelled_run")]
    [DataRow("cancelled_job")]
    public async Task CompleteAsync_LostLeaseOrCancel_DoesNotVerifyOrPublishArtifact(string reason)
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = fixture.AddJob(run);
        var token = job.LeaseToken!.Value;
        if (reason == "expired") job.LeaseExpiresAt = WorkflowTestData.Now.AddTicks(-1).UtcDateTime;
        if (reason == "at_expiry") job.LeaseExpiresAt = WorkflowTestData.Now.UtcDateTime;
        if (reason == "wrong_token") token = Guid.NewGuid();
        if (reason == "cancelled_run") run.Status = "cancelled";
        if (reason == "cancelled_job") job.Status = "cancelled";
        var result = await fixture.MediaService.CompleteAsync(job.Id, new(token, WorkflowTestData.OutputKey(job, "video"), "1", null, null, WorkflowTestData.Qa()), default);
        result.Error.Code.Should().Be("LeaseLost");
        fixture.Storage.Verify(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HeartbeatAsync_ActiveLease_KeepsProgressMonotonicAndRenewsFromClock()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = fixture.AddJob(run);
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.MediaService.HeartbeatAsync(job.Id, new(job.LeaseToken!.Value, "Rendering", 20), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        job.Progress.Should().Be(40);
        job.HeartbeatAt.Should().Be(WorkflowTestData.Now.AddSeconds(30).UtcDateTime);
        job.LeaseExpiresAt.Should().Be(WorkflowTestData.Now.AddMinutes(2).AddSeconds(30).UtcDateTime);
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).Progress.Should().Be(40);
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).Stage.Should().Be("Rendering");
        fixture.State.Verify(x => x.LockRunAsync(run.Id, cancellation.Token), Times.Once);
        fixture.Unit.Verify(x => x.SaveChangesAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    [DataRow(-1, "Rendering")]
    [DataRow(101, "Rendering")]
    [DataRow(50, "Unknown")]
    public async Task HeartbeatAsync_InvalidCheckpoint_DoesNotTouchLease(int progress, string stage)
    {
        var fixture = new WorkflowTestData();
        var result = await fixture.MediaService.HeartbeatAsync(Guid.NewGuid(), new(Guid.NewGuid(), stage, progress), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("Invalid progress checkpoint.");
        fixture.Unit.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task HeartbeatAsync_ExpiredLease_DoesNotReviveJob()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = fixture.AddJob(run);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        var result = await fixture.MediaService.HeartbeatAsync(job.Id, new(job.LeaseToken!.Value, "Rendering", 90), default);
        result.Error.Code.Should().Be("LeaseLost");
        job.Progress.Should().Be(40);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ClaimAsync_EmptyQueue_ReturnsNoWorkAndCommitsClaimTransaction()
    {
        var fixture = new WorkflowTestData();
        var result = await fixture.MediaService.ClaimAsync(default);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Storage.Verify(x => x.CreateUploadGrant(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ClaimAsync_RenderJob_ReturnsUniqueSourcesAndAttemptScopedUploadKeys()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single();
        SetupClaim(fixture);
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), "image", It.IsAny<string>(), false)).Returns("https://example.invalid/signed-image");
        fixture.Storage.Setup(x => x.CreateUploadGrant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string key, string type) => new UploadGrant("https://example.invalid/upload", new Dictionary<string, string>(), key, type));
        var result = await fixture.MediaService.ClaimAsync(default);
        var claimed = result.Value!;
        claimed.Id.Should().Be(job.Id);
        claimed.LeaseToken.Should().Be(job.LeaseToken!.Value);
        claimed.Assets.Should().HaveCount(1);
        claimed.Assets[0].FileName.Should().Be($"mockups/{fixture.AssetRows.Single().Id}.png");
        claimed.Uploads.Keys.Should().BeEquivalentTo("video", "thumbnail");
        claimed.Uploads["video"].StorageKey.Should().Be(WorkflowTestData.OutputKey(job, "video"));
        claimed.Uploads["thumbnail"].ResourceType.Should().Be("image");
    }

    [TestMethod]
    public async Task ClaimAsync_SourceApprovalChanged_DoesNotReturnUploadGrant()
    {
        var fixture = RenderFixture();
        fixture.AssetRows.Single().ApprovalStatus = "rejected";
        SetupClaim(fixture);
        var result = await fixture.MediaService.ClaimAsync(default);
        result.Value.Should().BeNull();
        fixture.JobRows.Single().Status.Should().Be("queued");
        fixture.JobRows.Single().LeaseToken.Should().BeNull();
        fixture.Storage.Verify(x => x.CreateUploadGrant(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteAsync_ValidVideoAndThumbnail_PublishesCandidateOnlyToReviewCheckpoint()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single();
        var completion = Completion(job);
        SetupVerification(fixture);
        var result = await fixture.MediaService.CompleteAsync(job.Id, completion, default);
        result.IsSuccess.Should().BeTrue();
        var video = fixture.VideoRows.Single();
        video.Status.Should().Be("completed");
        video.StorageKey.Should().Be(completion.StorageKey);
        video.ThumbnailStorageKey.Should().Be(completion.ThumbnailStorageKey);
        video.ApprovalStatus.Should().Be("pending");
        WorkflowJson.Read<MediaQa>(video.QaResult).FullDecode.Should().BeTrue();
        job.Status.Should().Be("succeeded");
        job.LeaseToken.Should().BeNull();
        job.LeaseExpiresAt.Should().BeNull();
        var run = fixture.RunRows.Single();
        run.Status.Should().Be("waiting_for_review");
        run.Revision.Should().Be(8);
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("waiting_for_review");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompleteAsync_WrongStorageKey_DoesNotAcceptAnotherAttemptArtifact()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single();
        var result = await fixture.MediaService.CompleteAsync(job.Id, Completion(job) with { StorageKey = "workflow-media/another-attempt/video" }, default);
        result.Error.Message.Should().Contain("upload grant");
        fixture.VideoRows.Single().Status.Should().Be("queued");
        fixture.Storage.Verify(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteAsync_TechnicalQaFails_DoesNotCompleteVideoOrAdvanceRun()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single();
        SetupVerification(fixture);
        var result = await fixture.MediaService.CompleteAsync(job.Id, Completion(job) with { Qa = WorkflowTestData.Qa() with { HasAudio = true } }, default);
        result.Error.Message.Should().Be("Video failed technical QA.");
        fixture.VideoRows.Single().Status.Should().Be("queued");
        fixture.RunRows.Single().Status.Should().Be("running");
        job.Status.Should().Be("leased");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteAsync_SourceRevisionChangedDuringRender_RejectsCandidate()
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single();
        fixture.AssetRows.Single().MetadataRevision++;
        SetupVerification(fixture);
        var result = await fixture.MediaService.CompleteAsync(job.Id, Completion(job), default);
        result.Error.Code.Should().Be("SourceChanged");
        fixture.VideoRows.Single().Status.Should().Be("queued");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(1, "queued")]
    [DataRow(2, "queued")]
    [DataRow(3, "failed")]
    public async Task FailAsync_AttemptLimit_RequeuesWithBackoffOrFailsCheckpoint(int attempt, string expected)
    {
        var fixture = RenderFixture();
        var job = fixture.JobRows.Single(); job.Attempt = attempt;
        var result = await fixture.MediaService.FailAsync(job.Id, new(job.LeaseToken!.Value, "SECRET_PROVIDER_URL"), default);
        result.IsSuccess.Should().BeTrue();
        job.Status.Should().Be(expected);
        job.LeaseToken.Should().BeNull();
        job.AvailableAt.Should().Be(WorkflowTestData.Now.AddSeconds(attempt * 5).UtcDateTime);
        job.ErrorMessage.Should().NotContain("SECRET_PROVIDER_URL");
        fixture.RunRows.Single().Status.Should().Be(attempt == 3 ? "failed" : "running");
        fixture.VideoRows.Single().Status.Should().Be(attempt == 3 ? "failed" : "queued");
    }

    private static WorkflowTestData RenderFixture()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        run.Status = "running";
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status = "pending";
        fixture.VideoRows.Single().Status = "queued";
        var board = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value;
        var job = fixture.AddJob(run);
        job.Payload = WorkflowJson.Write(new RenderJobPayload(fixture.VideoRows.Single().Id, board));
        return fixture;
    }

    private static async Task<WorkflowTestData> ExportFixtureAsync()
    {
        var fixture = new WorkflowTestData();
        var run = WorkflowRunServiceTests.PrepareReview(fixture);
        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", fixture.VideoRows.Single().Id, 2), default);
        var job = fixture.JobRows.Single(); job.Status = "leased"; job.LeaseToken = Guid.NewGuid();
        job.LeaseExpiresAt = WorkflowTestData.Now.AddMinutes(2).UtcDateTime; job.Attempt = 1;
        return fixture;
    }

    private static void SetupClaim(WorkflowTestData fixture)
    {
        fixture.State.Setup(x => x.ClaimAsync(It.IsAny<DateTime>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime _, Guid token, CancellationToken _) =>
            {
                var job = fixture.JobRows.Single(); job.LeaseToken = token; return job;
            });
    }

    private static void SetupVerification(WorkflowTestData fixture) => fixture.Storage
        .Setup(x => x.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private static JobCompletion Completion(APCS.Domain.Entities.MediaJob job) => new(job.LeaseToken!.Value,
        WorkflowTestData.OutputKey(job, "video"), "1", WorkflowTestData.OutputKey(job, "thumbnail"), "1", WorkflowTestData.Qa());
}
