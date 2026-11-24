using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiShotJobServiceTests
{
    private const string Operation = "models/veo/operations/abc";
    private const string ClipUrl = "https://generativelanguage.googleapis.com/clip";

    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IVeoVideoProvider> provider = new();
    private readonly Mock<IAiVideoCredentialResolver> videoCredentials = new();
    private MediaJob job = null!;
    private MockupImage source = null!;

    [TestMethod]
    public async Task StartAsync_PendingScene_SubmitsOnceAndRemembersTheOperation()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync("plain-key", "https://signed.example/source", "Move gently.", "9:16", 8, true, It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Value.Status.Should().Be("polling");
        State().Status.Should().Be("polling");
        State().OperationName.Should().Be(Operation);

        // The worker asks again after a restart: the paid request is not sent a second time.
        (await service.StartAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("polling");
        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_StoredScene_IsNotSubmittedAgain()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Status: "stored"));

        (await service.StartAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("stored");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartAsync_SubmissionInterruptedEarlier_IsReportedAsUncertain()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Status: "submitting"));

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoSubmissionUncertain");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartAsync_WrongLeaseSceneOrJob_IsRejected()
    {
        var service = Service();

        (await service.StartAsync(job.Id, 0, new(Guid.NewGuid()), default)).Error.Code.Should().Be("LeaseLost");
        (await service.StartAsync(job.Id, 5, Lease(), default)).Error.Code.Should().Be("LeaseLost");
        (await service.StartAsync(Guid.NewGuid(), 0, Lease(), default)).Error.Code.Should().Be("LeaseLost");
    }

    [TestMethod]
    public async Task StartAsync_SourceNoLongerApproved_DoesNotSubmit()
    {
        var service = Service();
        source.ApprovalStatus = "pending";

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("SourceChanged");
        provider.VerifyNoOtherCalls();
    }

    // The scene's own source (the approved keyframe) is checked by id, hash, revision and approval. Editing the mock-up
    // it was made from afterwards does not make it unsafe, and refusing it could strand a run that was already paid for.
    [TestMethod]
    public async Task StartAsync_StudioFrameOriginalSourceChangedAfterQuote_StillSubmitsTheApprovedFrame()
    {
        var service = Service();
        var original = source;
        var frame = WorkflowTestData.Asset(original.ProductId, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ApprovedRevision = 1;
        frame.ContentHash = new string('b', 64);
        var aspect = VideoOutputFormats.Resolve(VideoOutputFormats.Default).AspectRatio;
        frame.Regions = AiStudioFrameIdentity.SerializeRegions(new(
            AiStudioFrameIdentity.Fingerprint(original, AiStudioFrameIdentity.HangingWindow, aspect), original.Id,
            original.ContentHash!, original.MetadataRevision, AiStudioFrameIdentity.HangingWindow, aspect, AiStudioFrameIdentity.PromptVersion));
        fixture.AssetRows.Add(frame);
        var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
        var oldScene = payload.Storyboard.Scenes.Single();
        var scene = oldScene with { MockupId = frame.Id, SourceRevision = frame.MetadataRevision,
            SourceHash = frame.ContentHash!, Role = frame.Role, AiTemplateCode = "cinematic_product_showcase" };
        var oldShot = payload.Storyboard.AiShots!.Single();
        var shot = oldShot with { MockupId = frame.Id, SourceHash = frame.ContentHash! };
        job.Payload = WorkflowJson.Write(payload with
        {
            Storyboard = payload.Storyboard with { Scenes = [scene], AiShots = [shot] }
        });
        original.ContentHash = new string('d', 64);
        original.MetadataRevision++;
        original.ApprovedRevision++;
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Value.Status.Should().Be("polling");
        State().Status.Should().Be("polling");
        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow(400, "rejected this source or prompt")]
    [DataRow(403, "access denied")]
    public async Task StartAsync_GoogleRejectsTheRequest_MarksTheSceneFailed(int status, string message)
    {
        var service = Service();
        Refuse(status);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Message.Should().Contain(message);
        State().Status.Should().Be("failed");
        // What the provider said stays with the scene, for the run to show.
        State().Failure.Should().Contain(message).And.EndWith("Google Veo: The image is not usable.");
    }

    [TestMethod]
    [DataRow(429, "quota")]
    [DataRow(503, "overloaded")]
    [DataRow(500, "temporary error")]
    [DataRow(504, "temporary error")]
    public async Task StartAsync_GoogleIsBusy_LeavesTheSceneReadyToSubmitAgain(int status, string message)
    {
        var service = Service();
        Refuse(status);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        // An HTTP error means Google made no operation, so this is never an uncertain paid submission.
        result.Error.Code.Should().Be("VeoBusy");
        result.Error.Message.Should().Contain(message);
        State().Status.Should().Be("pending");
        State().OperationName.Should().BeNull();
        State().Failure.Should().Contain(message).And.EndWith("Google Veo: The image is not usable.");
    }

    [TestMethod]
    public async Task StartAsync_OtherRefusal_FailsTheSceneInsteadOfLeavingItUncertain()
    {
        var service = Service();
        Refuse(409);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Type.Should().Be(ErrorType.Validation);
        State().Status.Should().Be("failed");
        State().Failure.Should().Contain("409");
    }

    [TestMethod]
    public async Task StartAsync_AcceptedButUnreadable_IsUncertainAndSaysWhy()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiVideoSubmissionUnknownException("Veo accepted the request, but did not name an operation to follow."));

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoSubmissionUncertain");
        State().Status.Should().Be("submitting");
        State().Failure.Should().Contain("did not name an operation");
    }

    [TestMethod]
    public async Task StartAsync_UnknownOutcome_IsNeverResubmittedAutomatically()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoSubmissionUncertain");
        State().Status.Should().Be("submitting");
    }

    [TestMethod]
    public async Task StartAsync_NoConnectedKey_DoesNotCallGoogleAndSaysWhy()
    {
        var service = Service(connected: false);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("API key");
        State().Status.Should().Be("failed");
        State().Failure.Should().Contain("Reconnect");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartAsync_SourceCouldNotBePrepared_LeavesTheSceneReadyToSubmitAgain()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiVideoNotSubmittedException("Timed out reading the approved source image."));

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        // Google received nothing, so this is not an uncertain paid submission.
        result.Error.Code.Should().Be("AiVideoNotSubmitted");
        State().Status.Should().Be("pending");
        State().OperationName.Should().BeNull();
        State().Failure.Should().Be("Timed out reading the approved source image.");
    }

    [TestMethod]
    public async Task PollAsync_OperationStillRunning_KeepsPolling()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(false, null, null));

        (await service.PollAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("polling");
        State().Status.Should().Be("polling");
    }

    [TestMethod]
    public async Task PollAsync_ClipFinished_RemembersWhereToDownloadIt()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(true, ClipUrl, null));

        (await service.PollAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("ready");
        State().Status.Should().Be("ready");
        State().DownloadUri.Should().Be(ClipUrl);

        // Already known to be ready: Google is not asked again.
        (await service.PollAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("ready");
        provider.Verify(x => x.PollAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PollAsync_GoogleFinishedWithoutAVideo_MarksTheSceneFailedWithItsReason()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(true, null, "The image shows a minor."));

        var result = await service.PollAsync(job.Id, 0, Lease(), default);

        result.Error.Message.Should().Be("The image shows a minor.");
        State().Status.Should().Be("failed");
        State().Failure.Should().Be("The image shows a minor.");
        State().Resubmissions.Should().Be(0);
    }

    [TestMethod]
    public async Task PollAsync_ClipDroppedWithoutCharge_IsSubmittedOnceMoreAndThenLeftToTheSeller()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VeoOperationResult(true, null, "Audio issue. You have not been charged for this attempt.", NotCharged: true));
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("models/veo/operations/second");

        // The first drop puts the scene back, so the next start by the worker sends it again.
        var first = await service.PollAsync(job.Id, 0, Lease(), default);
        first.Value.Status.Should().Be("pending");
        State().Status.Should().Be("pending");
        State().OperationName.Should().BeNull();
        State().Resubmissions.Should().Be(1);
        (await service.StartAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("polling");
        State().OperationName.Should().Be("models/veo/operations/second");

        // The second drop is final: no third request without the seller.
        var second = await service.PollAsync(job.Id, 0, Lease(), default);
        second.IsFailure.Should().BeTrue();
        State().Status.Should().Be("failed");
        State().Failure.Should().Contain("Audio issue.");
        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PollAsync_GoogleUnreachable_CanBeCheckedAgain()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

        (await service.PollAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoProviderUnavailable");
        State().Status.Should().Be("polling");
    }

    [TestMethod]
    public async Task PollAsync_NothingWasSubmitted_IsReportedAsUncertain()
    {
        var service = Service();

        (await service.PollAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoSubmissionUncertain");
        (await service.PollAsync(job.Id, 0, new(Guid.NewGuid()), default)).Error.Code.Should().Be("LeaseLost");
    }

    [TestMethod]
    public async Task DownloadAsync_ReadyClip_ReturnsItsBytes()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        provider.Setup(x => x.DownloadAsync("plain-key", ClipUrl, It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);

        (await service.DownloadAsync(job.Id, 0, Lease(), default)).Value.Should().Equal(1, 2, 3);
    }

    [TestMethod]
    public async Task DownloadAsync_ClipNotReady_Fails()
    {
        var service = Service();

        (await service.DownloadAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("NotReady");
        (await service.DownloadAsync(job.Id, 0, new(Guid.NewGuid()), default)).Error.Code.Should().Be("LeaseLost");
    }

    [TestMethod]
    public async Task DownloadAsync_GoogleUnreachable_Fails()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        provider.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

        (await service.DownloadAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("AiVideoDownloadFailed");
    }

    [TestMethod]
    public async Task StoredAsync_VerifiedUpload_KeepsTheStoredClipAndForgetsTheGoogleUrl()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        var key = AiShotJobService.OutputKey(job, job.LeaseToken!.Value, 0);
        fixture.Storage.Setup(x => x.VerifyAsync(key, "video", "42", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, key, "42"), default)).IsSuccess.Should().BeTrue();

        State().Status.Should().Be("stored");
        State().StorageKey.Should().Be(key);
        State().StorageVersion.Should().Be("42");
        State().DownloadUri.Should().BeNull();
    }

    [TestMethod]
    public async Task StoredAsync_VerifiedUpload_NotifiesOnlyAfterTransactionCommit()
    {
        var notifier = new Mock<ISceneVideoNotifier>();
        var committed = false;
        fixture.Transaction.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => committed = true).Returns(Task.CompletedTask);
        notifier.Setup(x => x.ChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => committed.Should().BeTrue()).Returns(Task.CompletedTask);
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"), notifier: notifier.Object);
        var key = AiShotJobService.OutputKey(job, job.LeaseToken!.Value, 0);
        fixture.Storage.Setup(x => x.VerifyAsync(key, "video", "42", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, key, "42"), default)).IsSuccess.Should().BeTrue();

        notifier.Verify(x => x.ChangedAsync(job.WorkflowRunId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task StoredAsync_UnexpectedKeyOrUnverifiedUpload_IsRejected()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        var key = AiShotJobService.OutputKey(job, job.LeaseToken!.Value, 0);

        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, "somewhere/else", "42"), default)).IsFailure.Should().BeTrue();
        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, key, "42"), default)).IsFailure.Should().BeTrue();
        (await service.StoredAsync(job.Id, 0, new(Guid.NewGuid(), key, "42"), default)).Error.Code.Should().Be("LeaseLost");
        State().Status.Should().Be("ready");
    }

    [TestMethod]
    public async Task StartAsync_NewMiniMaxSnapshot_UsesCapturedMiniMaxStrategyAndKey()
    {
        var minimax = new Mock<IAiVideoGenerationStrategy>();
        minimax.Setup(x => x.StartAsync("plain-key", It.IsAny<string>(), "Move gently.", "9:16", 8, true,
                It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("minimax-task-123");
        var factory = new Mock<IAiVideoGenerationStrategyFactory>();
        factory.Setup(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey)).Returns(minimax.Object);
        var service = Service(providerKey: AiVideoModel.ProviderKey, modelKey: AiVideoModel.ModelKey,
            model: AiVideoModel.ModelName, providerFactory: factory.Object);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.IsSuccess.Should().BeTrue();
        State().OperationName.Should().Be("minimax-task-123");
        provider.VerifyNoOtherCalls();
        factory.Verify(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_NewMiniMaxSnapshotWithoutSystemKey_DoesNotSubmit()
    {
        var minimax = new Mock<IAiVideoGenerationStrategy>();
        var factory = new Mock<IAiVideoGenerationStrategyFactory>();
        factory.Setup(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey)).Returns(minimax.Object);
        var service = Service(connected: false, providerKey: AiVideoModel.ProviderKey, modelKey: AiVideoModel.ModelKey,
            model: AiVideoModel.ModelName, providerFactory: factory.Object);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("system MiniMax API key");
        minimax.VerifyNoOtherCalls();
    }

    // The platform holds several MiniMax keys; the pool hands out the second one, and finds it again by its id.
    private static (Mock<ISystemApiKeyPool> Pool, Mock<ISystemApiKeyLease> Lease) PooledMiniMaxKey(bool usedUp = false)
    {
        var lease = new Mock<ISystemApiKeyLease>();
        lease.SetupGet(x => x.ApiKey).Returns("pool-key-2");
        lease.SetupGet(x => x.KeyId).Returns("0123456789ab");
        lease.SetupGet(x => x.Provider).Returns(AiVideoModel.ProviderKey);
        var pool = new Mock<ISystemApiKeyPool>();
        pool.Setup(x => x.HasKeys(AiVideoModel.ProviderKey)).Returns(true);
        pool.Setup(x => x.AcquireAsync(AiVideoModel.ProviderKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(usedUp ? null : lease.Object);
        pool.Setup(x => x.FindKey(AiVideoModel.ProviderKey, "0123456789ab")).Returns("pool-key-2");
        return (pool, lease);
    }

    [TestMethod]
    public async Task StartAsync_MiniMaxOnThePlatformsKeys_SubmitsWithAPooledKeyAndAsksAboutItWithTheSameKey()
    {
        var minimax = new Mock<IAiVideoGenerationStrategy>();
        minimax.Setup(x => x.StartAsync("pool-key-2", It.IsAny<string>(), "Move gently.", "9:16", 8, true,
                It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("minimax-task-123");
        minimax.Setup(x => x.PollAsync("pool-key-2", "minimax-task-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VeoOperationResult(false, null, null));
        var factory = new Mock<IAiVideoGenerationStrategyFactory>();
        factory.Setup(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey)).Returns(minimax.Object);
        var (pool, lease) = PooledMiniMaxKey();
        var service = Service(providerKey: AiVideoModel.ProviderKey, modelKey: AiVideoModel.ModelKey,
            model: AiVideoModel.ModelName, providerFactory: factory.Object, systemKeys: pool.Object);

        (await service.StartAsync(job.Id, 0, Lease(), default)).IsSuccess.Should().BeTrue();
        State().KeyId.Should().Be("0123456789ab");
        (await service.PollAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("polling");

        minimax.Verify(x => x.PollAsync("pool-key-2", "minimax-task-123", It.IsAny<CancellationToken>()), Times.Once);
        lease.Verify(x => x.RefundAsync(It.IsAny<CancellationToken>()), Times.Never, "the clip is being made and paid for");
        lease.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_EveryPlatformMiniMaxKeyUsedUpToday_SubmitsNothingAndSaysSo()
    {
        var minimax = new Mock<IAiVideoGenerationStrategy>();
        var factory = new Mock<IAiVideoGenerationStrategyFactory>();
        factory.Setup(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey)).Returns(minimax.Object);
        var (pool, _) = PooledMiniMaxKey(usedUp: true);
        var service = Service(providerKey: AiVideoModel.ProviderKey, modelKey: AiVideoModel.ModelKey,
            model: AiVideoModel.ModelName, providerFactory: factory.Object, systemKeys: pool.Object);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Code.Should().Be("AiVideoDailyLimitReached");
        State().Status.Should().Be("pending");
        minimax.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(401, true)]
    [DataRow(400, false)]
    [DataRow(429, false)]
    public async Task StartAsync_PlatformMiniMaxKeyRefused_GivesTheUseBackAndPassesARefusedKeyOver(int status, bool keyRefused)
    {
        var minimax = new Mock<IAiVideoGenerationStrategy>();
        minimax.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiVideoProviderException(status, "refused"));
        var factory = new Mock<IAiVideoGenerationStrategyFactory>();
        factory.Setup(x => x.Resolve(AiVideoModel.ProviderKey, AiVideoModel.ModelKey)).Returns(minimax.Object);
        var (pool, lease) = PooledMiniMaxKey();
        var service = Service(providerKey: AiVideoModel.ProviderKey, modelKey: AiVideoModel.ModelKey,
            model: AiVideoModel.ModelName, providerFactory: factory.Object, systemKeys: pool.Object);

        (await service.StartAsync(job.Id, 0, Lease(), default)).IsFailure.Should().BeTrue();

        lease.Verify(x => x.DisableForTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), keyRefused ? Times.Once() : Times.Never());
        lease.Verify(x => x.RefundAsync(It.IsAny<CancellationToken>()), keyRefused ? Times.Never() : Times.Once());
    }

    // The API makes the clips of a job in generating_clips itself: no worker holds a lease on it.
    private AiShotJobService ClipPhaseService(AiShotJobState? state = null, ISceneVideoNotifier? notifier = null,
        AiVideoCallbackUrls? callbacks = null)
    {
        var service = Service(state, notifier: notifier, callbacks: callbacks);
        job.Status = AiClipPhase.JobStatus;
        job.LeaseToken = null;
        job.LeaseExpiresAt = null;
        return service;
    }

    [TestMethod]
    public async Task StartClipAsync_JobMakingItsClips_SubmitsWithoutALeaseAndRemembersWhenItWasSubmitted()
    {
        var service = ClipPhaseService();
        provider.Setup(x => x.StartAsync("plain-key", "https://signed.example/source", "Move gently.", "9:16", 8, true, It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation);

        var result = await service.StartClipAsync(job.Id, 0, default);

        result.Value.Status.Should().Be("polling");
        State().OperationName.Should().Be(Operation);
        State().SubmittedAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        State().NextAttemptAt.Should().BeNull();
    }

    private static AiVideoCallbackUrls Callbacks() =>
        new(Options.Create(new AiVideoCallbackSettings { BaseUrl = "https://shop.example", Secret = "test-secret" }));

    [TestMethod]
    public async Task StartClipAsync_CallbacksOn_AsksTheProviderToCallBackAtTheScenesSignedUrl()
    {
        var callbacks = Callbacks();
        var service = ClipPhaseService(callbacks: callbacks);
        string? sentTo = null;
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, int, bool, Region?, string?, CancellationToken>((_, _, _, _, _, _, _, url, _) => sentTo = url)
            .ReturnsAsync(Operation);

        (await service.StartClipAsync(job.Id, 0, default)).Value.Status.Should().Be("polling");

        sentTo.Should().Be(callbacks.For(job.Id, 0)).And.StartWith($"https://shop.example/api/webhooks/minimax/{job.Id:N}/0/");
        State().Callback.Should().BeTrue();
    }

    [TestMethod]
    public async Task StartAsync_WorkerHoldingALease_IsNeverCalledBackAbout()
    {
        var service = Service(callbacks: Callbacks());
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation);

        (await service.StartAsync(job.Id, 0, Lease(), default)).IsSuccess.Should().BeTrue();

        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        State().Callback.Should().BeFalse();
    }

    [TestMethod]
    public async Task PollClipAsync_SceneThatCallsBackStillBeingMade_RemembersWhenItWasAskedAndSpendsTheCall()
    {
        var announced = fixture.Time.GetUtcNow().UtcDateTime.AddSeconds(-3);
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, Status: "polling", Callback: true, AnnouncedAt: announced));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(false, null, null));

        (await service.PollClipAsync(job.Id, 0, default)).Value.Status.Should().Be("polling");

        State().PolledAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        State().AnnouncedAt.Should().BeNull("a call that came too early must not make every pass ask again");
    }

    [TestMethod]
    public async Task PollClipAsync_SceneWithoutACallbackStillBeingMade_WritesNothing()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(false, null, null));

        await service.PollClipAsync(job.Id, 0, default);

        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AnnounceClipAsync_OperationTheSceneWaitsOn_MarksItToBeAskedAboutAtOnce()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, Status: "polling", Callback: true));

        (await service.AnnounceClipAsync(job.Id, 0, Operation, default)).Value.Should().BeTrue();

        State().AnnouncedAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow("polling", "another-operation")]
    [DataRow("ready", Operation)]
    [DataRow("stored", Operation)]
    public async Task AnnounceClipAsync_OperationTheSceneNoLongerWaitsOn_ChangesNothing(string status, string operation)
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, Status: status, Callback: true));

        (await service.AnnounceClipAsync(job.Id, 0, operation, default)).Value.Should().BeFalse();

        State().AnnouncedAt.Should().BeNull();
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AnnounceClipAsync_JobLeasedToAWorker_ChangesNothing()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));

        (await service.AnnounceClipAsync(job.Id, 0, Operation, default)).Value.Should().BeFalse();
        State().AnnouncedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task StartClipAsync_JobLeasedToAWorker_IsRefusedAndNothingIsSubmitted()
    {
        var service = ClipPhaseService();
        job.Status = "leased";

        (await service.StartClipAsync(job.Id, 0, default)).Error.Code.Should().Be("LeaseLost");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartClipAsync_RunNoLongerRunning_IsRefusedAndNothingIsSubmitted()
    {
        var service = ClipPhaseService();
        fixture.RunRows.Single().Status = "cancelled";

        (await service.StartClipAsync(job.Id, 0, default)).Error.Code.Should().Be("LeaseLost");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartAsync_JobInTheClipPhase_IsNotSubmittedByAWorkerLease()
    {
        var service = ClipPhaseService();
        job.LeaseToken = Guid.NewGuid();

        (await service.StartAsync(job.Id, 0, new(job.LeaseToken.Value), default)).Error.Code.Should().Be("LeaseLost");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task StartClipAsync_ProviderBusy_PutsTheSceneBackWithATimeToTryAgain()
    {
        var service = ClipPhaseService();
        Refuse(429);

        (await service.StartClipAsync(job.Id, 0, default)).Error.Code.Should().Be("VeoBusy");

        State().Status.Should().Be("pending");
        State().BusyAttempts.Should().Be(1);
        State().NextAttemptAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime.AddSeconds(70));
        // The next refusal waits longer.
        (await service.StartClipAsync(job.Id, 0, default)).Error.Code.Should().Be("VeoBusy");
        State().BusyAttempts.Should().Be(2);
        State().NextAttemptAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime.AddSeconds(140));
    }

    [TestMethod]
    public async Task StartAsync_ProviderBusyForAWorker_KeepsNoBackoffOfItsOwn()
    {
        var service = Service();
        Refuse(429);

        await service.StartAsync(job.Id, 0, Lease(), default);

        State().Status.Should().Be("pending");
        State().BusyAttempts.Should().Be(0);
        State().NextAttemptAt.Should().BeNull();
    }

    [TestMethod]
    public async Task PollClipAsync_ClipFinished_RemembersWhereToDownloadIt()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync("plain-key", Operation, It.IsAny<CancellationToken>())).ReturnsAsync(new VeoOperationResult(true, ClipUrl, null));

        (await service.PollClipAsync(job.Id, 0, default)).Value.Status.Should().Be("ready");
        State().DownloadUri.Should().Be(ClipUrl);
    }

    [TestMethod]
    public async Task StoreClipAsync_ReadyClip_DownloadsItStoresItAndForgetsTheProviderUrl()
    {
        var notifier = new Mock<ISceneVideoNotifier>();
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"), notifier.Object);
        provider.Setup(x => x.DownloadAsync("plain-key", ClipUrl, It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        string? storedKey = null;
        fixture.Storage.Setup(x => x.UploadVideoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, CancellationToken>((_, key, _) => storedKey = key).ReturnsAsync("42");
        fixture.Storage.Setup(x => x.VerifyAsync(It.IsAny<string>(), "video", "42", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await service.StoreClipAsync(job.Id, 0, default)).IsSuccess.Should().BeTrue();

        State().Status.Should().Be("stored");
        State().StorageKey.Should().Be(storedKey).And.StartWith($"workflow-media/{job.UserId}/{job.WorkflowRunId}/{job.Id}/").And.EndWith("/ai-shot-0");
        State().StorageVersion.Should().Be("42");
        State().DownloadUri.Should().BeNull();
        notifier.Verify(x => x.ChangedAsync(job.WorkflowRunId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task StoreClipAsync_AlreadyStored_DoesNothingMore()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Status: "stored"));

        (await service.StoreClipAsync(job.Id, 0, default)).IsSuccess.Should().BeTrue();
        provider.VerifyNoOtherCalls();
        fixture.Storage.Verify(x => x.UploadVideoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StoreClipAsync_ClipNotReady_Fails()
    {
        var service = ClipPhaseService();

        (await service.StoreClipAsync(job.Id, 0, default)).Error.Code.Should().Be("NotReady");
    }

    [TestMethod]
    public async Task StoreClipAsync_StorageCannotVerifyTheUpload_LeavesTheSceneReadyToTryAgain()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        provider.Setup(x => x.DownloadAsync("plain-key", ClipUrl, It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        fixture.Storage.Setup(x => x.UploadVideoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("42");
        fixture.Storage.Setup(x => x.VerifyAsync(It.IsAny<string>(), "video", "42", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        (await service.StoreClipAsync(job.Id, 0, default)).Error.Code.Should().Be("AiVideoStoreFailed");

        State().Status.Should().Be("ready");
        State().DownloadUri.Should().Be(ClipUrl);
    }

    [TestMethod]
    public async Task StoreClipAsync_ProviderDownloadFails_LeavesTheSceneReadyToTryAgain()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        provider.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

        (await service.StoreClipAsync(job.Id, 0, default)).Error.Code.Should().Be("AiVideoDownloadFailed");
        State().Status.Should().Be("ready");
    }

    [TestMethod]
    public async Task StoreClipAsync_EmptyClip_IsNotStored()
    {
        var service = ClipPhaseService(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        provider.Setup(x => x.DownloadAsync("plain-key", ClipUrl, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        (await service.StoreClipAsync(job.Id, 0, default)).Error.Code.Should().Be("AiVideoDownloadFailed");
        fixture.Storage.Verify(x => x.UploadVideoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AiShotLeaseRequest Lease() => new(job.LeaseToken!.Value);

    private AiShotJobState State() => WorkflowJson.Read<AiRenderJobPayload>(job.Payload).AiShotStates.Single();

    private void Refuse(int status) =>
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<Region?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VeoProviderException(status, "The image is not usable."));

    private AiShotJobService Service(AiShotJobState? state = null, bool connected = true,
        string providerKey = VeoVideoModel.ProviderKey, string modelKey = VeoVideoModel.ModelKey,
        string model = "veo", IAiVideoGenerationStrategyFactory? providerFactory = null, ISceneVideoNotifier? notifier = null,
        ISystemApiKeyPool? systemKeys = null, AiVideoCallbackUrls? callbacks = null)
    {
        var product = fixture.AddProduct();
        var run = fixture.AddRun(product);
        source = fixture.AddAsset(product, "Lifestyle");
        job = fixture.AddJob(run);
        var scene = new ScenePlan(source.Id, source.MetadataRevision, source.ContentHash!, source.Role, 0, 240, new(0, 0, 1, 1), new(0, 0, 1, 1), "static", "cut", "", "ai_shot", []);
        var board = new Storyboard("auto", 2, 8, "tshirt", [scene], "board", OutputFormat: "portrait", Width: 1080, Height: 1920, AspectRatio: "9:16", Mode: "ai_shot",
            AiShots: [new(0, source.Id, source.ContentHash!, model, "lifestyle_motion", "Move gently.", 8, "fingerprint",
                providerKey, modelKey, 1, null,
                providerKey == AiVideoModel.ProviderKey ? AiVideoModel.Resolution : VeoVideoModel.Resolution,
                providerKey == AiVideoModel.ProviderKey ? AiVideoModel.PricePerSecondUsd : VeoVideoModel.PricePerSecondUsd)]);
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), board, [state ?? new AiShotJobState(0, "fingerprint")]));

        videoCredentials.Setup(x => x.ResolveAsync(WorkflowTestData.Owner, providerKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connected ? "plain-key" : null);
        fixture.Storage.Setup(x => x.SignRead(source.StorageKey, "image", source.StorageVersion, false)).Returns("https://signed.example/source");
        return new(fixture.Jobs.Object, fixture.Assets.Object, videoCredentials.Object, fixture.State.Object,
            fixture.Unit.Object, fixture.Storage.Object, provider.Object, fixture.Time, providerFactory, sceneNotifier: notifier,
            systemKeys: systemKeys, callbacks: callbacks);
    }
}
