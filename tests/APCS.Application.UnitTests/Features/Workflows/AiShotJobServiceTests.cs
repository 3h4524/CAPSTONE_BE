using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiShotJobServiceTests
{
    private const string Operation = "models/veo/operations/abc";
    private const string ClipUrl = "https://generativelanguage.googleapis.com/clip";

    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IVeoVideoProvider> provider = new();
    private readonly Mock<IApiKeyRepository> keys = new();
    private MediaJob job = null!;
    private MockupImage source = null!;

    [TestMethod]
    public async Task StartAsync_PendingScene_SubmitsOnceAndRemembersTheOperation()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync("plain-key", "https://signed.example/source", "Move gently.", "9:16", 8, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Operation);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Value.Status.Should().Be("polling");
        State().Status.Should().Be("polling");
        State().OperationName.Should().Be(Operation);

        // The worker asks again after a restart: the paid request is not sent a second time.
        (await service.StartAsync(job.Id, 0, Lease(), default)).Value.Status.Should().Be("polling");
        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoSubmissionUncertain");
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

    [TestMethod]
    public async Task StartAsync_StudioFrameOriginalSourceChangedAfterQuote_DoesNotSubmit()
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

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Code.Should().Be("SourceChanged");
        provider.VerifyNoOtherCalls();
        State().Status.Should().Be("pending");
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
        // What Google said stays with the scene, for the run to show.
        State().Failure.Should().Contain(message).And.EndWith("Google: The image is not usable.");
    }

    [TestMethod]
    [DataRow(429, "quota")]
    [DataRow(503, "overloaded")]
    public async Task StartAsync_GoogleIsBusy_LeavesTheSceneReadyToSubmitAgain(int status, string message)
    {
        var service = Service();
        Refuse(status);

        var result = await service.StartAsync(job.Id, 0, Lease(), default);

        result.Error.Code.Should().Be("VeoBusy");
        result.Error.Message.Should().Contain(message);
        State().Status.Should().Be("pending");
        State().OperationName.Should().BeNull();
    }

    [TestMethod]
    public async Task StartAsync_UnknownOutcome_IsNeverResubmittedAutomatically()
    {
        var service = Service();
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoSubmissionUncertain");
        State().Status.Should().Be("submitting");
    }

    [TestMethod]
    public async Task StartAsync_NoConnectedKey_DoesNotCallGoogle()
    {
        var service = Service(connected: false);

        (await service.StartAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoSubmissionUncertain");
        provider.VerifyNoOtherCalls();
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
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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
        provider.Verify(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PollAsync_GoogleUnreachable_CanBeCheckedAgain()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, Status: "polling"));
        provider.Setup(x => x.PollAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

        (await service.PollAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoUnavailable");
        State().Status.Should().Be("polling");
    }

    [TestMethod]
    public async Task PollAsync_NothingWasSubmitted_IsReportedAsUncertain()
    {
        var service = Service();

        (await service.PollAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoSubmissionUncertain");
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

        (await service.DownloadAsync(job.Id, 0, Lease(), default)).Error.Code.Should().Be("VeoDownloadFailed");
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
    public async Task StoredAsync_UnexpectedKeyOrUnverifiedUpload_IsRejected()
    {
        var service = Service(new AiShotJobState(0, "fingerprint", Operation, ClipUrl, Status: "ready"));
        var key = AiShotJobService.OutputKey(job, job.LeaseToken!.Value, 0);

        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, "somewhere/else", "42"), default)).IsFailure.Should().BeTrue();
        (await service.StoredAsync(job.Id, 0, new(job.LeaseToken!.Value, key, "42"), default)).IsFailure.Should().BeTrue();
        (await service.StoredAsync(job.Id, 0, new(Guid.NewGuid(), key, "42"), default)).Error.Code.Should().Be("LeaseLost");
        State().Status.Should().Be("ready");
    }

    private AiShotLeaseRequest Lease() => new(job.LeaseToken!.Value);

    private AiShotJobState State() => WorkflowJson.Read<AiRenderJobPayload>(job.Payload).AiShotStates.Single();

    private void Refuse(int status) =>
        provider.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new VeoProviderException(status, "The image is not usable."));

    private AiShotJobService Service(AiShotJobState? state = null, bool connected = true)
    {
        var product = fixture.AddProduct();
        var run = fixture.AddRun(product);
        source = fixture.AddAsset(product, "Lifestyle");
        job = fixture.AddJob(run);
        var scene = new ScenePlan(source.Id, source.MetadataRevision, source.ContentHash!, source.Role, 0, 240, new(0, 0, 1, 1), new(0, 0, 1, 1), "static", "cut", "", "ai_shot", []);
        var board = new Storyboard("auto", 2, 8, "tshirt", [scene], "board", OutputFormat: "portrait", Width: 1080, Height: 1920, AspectRatio: "9:16", Mode: "ai_shot",
            AiShots: [new(0, source.Id, source.ContentHash!, "veo", "lifestyle_motion", "Move gently.", 8, "fingerprint")]);
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), board, [state ?? new AiShotJobState(0, "fingerprint")]));

        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect("encrypted")).Returns("plain-key");
        keys.Setup(x => x.ListOwnedAsync(WorkflowTestData.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = "gemini", IsActive = true, LastCheckSucceeded = connected, KeyValueEncrypted = "encrypted" }]);
        fixture.Storage.Setup(x => x.SignRead(source.StorageKey, "image", source.StorageVersion, false)).Returns("https://signed.example/source");
        return new(fixture.Jobs.Object, fixture.Assets.Object, keys.Object, credentials.Object, fixture.State.Object,
            fixture.Unit.Object, fixture.Storage.Object, provider.Object, fixture.Time);
    }
}
