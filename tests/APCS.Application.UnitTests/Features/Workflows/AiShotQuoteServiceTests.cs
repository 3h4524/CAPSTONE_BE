using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiShotQuoteServiceTests
{
    [TestMethod]
    [DataRow("single_scene", 12, 1, 0.48)]
    [DataRow("full_video", 8, 1, 0.64)]
    [DataRow("full_video", 15, 2, 1.28)]
    public async Task QuoteAsync_MissingAiClips_ReturnsExactEstimatedCost(string coverage, int duration, int clips, double cost)
    {
        var context = CreateContext();
        var config = Config(context.Asset, coverage, duration);
        using var cancellation = new CancellationTokenSource();

        var result = await context.Service.QuoteAsync(context.Run.Id, new(config), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.NewClips.Should().Be(clips);
        result.Value.EstimatedCostUsd.Should().Be((decimal)cost);
        result.Value.Model.Should().Be(AiVideoModel.ModelName);
        result.Value.Token.Should().Be("signed-quote");
        result.Value.ExpiresAtUtc.Should().Be(context.Fixture.Time.GetUtcNow().AddMinutes(10));
        context.Claims.Should().Match<AiShotQuoteClaims>(x => x.RunId == context.Run.Id &&
            x.EstimatedCostUsd == (decimal)cost && !x.Regenerate && x.StoryboardFingerprint.Length == 64 &&
            x.ConfigFingerprint != null && x.PromptSnapshot != null && x.PromptSnapshot.Fingerprint == x.StoryboardFingerprint);
        context.VideoCredentials.Verify(x => x.ResolveAsync(WorkflowTestData.Owner, AiVideoModel.ProviderKey, cancellation.Token), Times.Once);
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Once);
    }

    [TestMethod]
    public async Task QuoteAsync_StoredMatchingClip_QuotesZeroWithoutRegeneration()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        var board = (await context.Fixture.Runtime.PlanAsync(context.Run, config, default)).Value;
        var shot = board.AiShots!.Single();
        var job = context.Fixture.AddJob(context.Run);
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), board,
            [new AiShotJobState(shot.SceneOrder, shot.Fingerprint, StorageKey: "private/shot.mp4", StorageVersion: "1", Status: "stored")]));

        var reuse = await context.Service.QuoteAsync(context.Run.Id, new(config), default);
        var regenerate = await context.Service.QuoteAsync(context.Run.Id, new(config, Regenerate: true), default);

        reuse.Value.NewClips.Should().Be(0);
        reuse.Value.EstimatedCostUsd.Should().Be(0m);
        regenerate.Value.NewClips.Should().Be(1);
        regenerate.Value.EstimatedCostUsd.Should().Be(.48m);
    }

    [TestMethod]
    public async Task QuoteAsync_PendingStudioFrame_IsRejectedBeforeSigningVideoQuote()
    {
        var context = CreateContext();
        var frame = WorkflowTestData.Asset(context.Asset.ProductId, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ApprovalStatus = "pending";
        frame.ApprovedRevision = null;
        frame.ContentHash = new string('b', 64);
        var aspect = VideoOutputFormats.Resolve(VideoOutputFormats.Default).AspectRatio;
        frame.Regions = AiStudioFrameIdentity.SerializeRegions(new(
            AiStudioFrameIdentity.Fingerprint(context.Asset, AiStudioFrameIdentity.HangingWindow, aspect), context.Asset.Id,
            context.Asset.ContentHash!, context.Asset.MetadataRevision, AiStudioFrameIdentity.HangingWindow, aspect, AiStudioFrameIdentity.PromptVersion));
        context.Fixture.AssetRows.Add(frame);
        var config = new GenerateVideoConfig(Mode: "ai_shot", AiScenes: [new(MockupId: context.Asset.Id)], AiStudioMode: "studio",
            AiStudioSourceMockupId: context.Asset.Id, AiStudioFrameId: frame.Id);

        var result = await context.Service.QuoteAsync(context.Run.Id, new(config), default);

        // A keyframe that is not approved cannot be selected at all.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("StudioFrameRequired");
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_MissingSystemMiniMaxKey_RejectsBeforeSigning()
    {
        var context = CreateContext(systemKeyConfigured: false);
        var result = await context.Service.QuoteAsync(context.Run.Id, new(Config(context.Asset)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("system MiniMax API key");
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_PreviewFingerprintChanged_RejectsBeforeCostConfirmation()
    {
        var context = CreateContext();

        var result = await context.Service.QuoteAsync(context.Run.Id,
            new(Config(context.Asset), ExpectedStoryboardFingerprint: "outdated-preview"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("StoryboardChanged");
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_ForeignRun_DoesNotReadOwnerKeyOrSources()
    {
        var context = CreateContext();
        context.Run.UserId = Guid.NewGuid();

        var result = await context.Service.QuoteAsync(context.Run.Id, new(Config(context.Asset)), default);

        result.Error.Code.Should().Be("RunNotFound");
        context.VideoCredentials.Verify(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Fixture.Assets.Verify(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MockupImage, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_RunningRun_RejectsBeforeReadingPaidKey()
    {
        var context = CreateContext();
        context.Run.Status = "running";

        var result = await context.Service.QuoteAsync(context.Run.Id, new(Config(context.Asset)), default);

        result.Error.Code.Should().Be("InvalidCheckpoint");
        context.VideoCredentials.Verify(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task VerifyAsync_ExpiredQuote_RejectsWithoutReplanning()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();
        context.Fixture.Time.Advance(TimeSpan.FromMinutes(11));

        var result = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Confirm");
    }

    [TestMethod]
    public async Task VerifyAsync_MissingOrDifferentRunToken_RejectsConfirmation()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();

        var missing = await context.Service.VerifyAsync(context.Run, config, null, false, null, default);
        context.Claims = context.Claims! with { RunId = Guid.NewGuid() };
        var foreignToken = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, null, default);

        missing.IsFailure.Should().BeTrue();
        foreignToken.IsFailure.Should().BeTrue();
        missing.Error.Message.Should().Contain("Confirm");
        foreignToken.Error.Message.Should().Contain("Confirm");
    }

    [TestMethod]
    public async Task VerifyAsync_RegenerationChoiceOrAHigherCostChanged_RejectsStaleQuote()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();

        var wrongChoice = await context.Service.VerifyAsync(context.Run, config, "signed-quote", true, null, default);
        var costlier = await context.Service.VerifyAsync(context.Run, Config(context.Asset, "full_video", 15), "signed-quote", false, null, default);

        wrongChoice.IsFailure.Should().BeTrue();
        costlier.Error.Code.Should().Be("AiShotQuoteChanged");
    }

    // Prompt snapshots record the approved source revision. A newer source must be reviewed before it can be rendered.
    [TestMethod]
    public async Task VerifyAsync_SourceChangedAtTheSameCost_RequiresAnotherReview()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();

        context.Asset.ContentHash = new string('b', 64);
        context.Asset.MetadataRevision++;
        context.Asset.ApprovedRevision++;
        var changedSource = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, null, default);

        changedSource.IsFailure.Should().BeTrue();
        changedSource.Error.Code.Should().Be("AiShotQuoteChanged");
    }

    [TestMethod]
    public async Task VerifyAsync_CurrentQuote_WritesTheSignedPromptSnapshotForTheRenderStep()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        await context.Service.QuoteAsync(context.Run.Id, new(config), default);

        var result = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, null, default);

        result.IsSuccess.Should().BeTrue();
        var generate = context.Fixture.NodeRows.Single(item => item.WorkflowRunId == context.Run.Id && item.NodeType == "generate-video");
        var snapshot = WorkflowJson.Read<AiShotPromptSnapshot>(generate.OutputSnapshot);
        snapshot.ConfigFingerprint.Should().Be(context.Claims!.ConfigFingerprint);
        snapshot.Storyboard.Fingerprint.Should().Be(context.Claims.PromptSnapshot!.Fingerprint);
    }

    private static GenerateVideoConfig Config(MockupImage source, string coverage = "single_scene", int duration = 12) =>
        new(Mode: "ai_shot", DurationSeconds: duration, AiShotOptions: new(Coverage: coverage, MockupId: source.Id));

    private static Context CreateContext(bool systemKeyConfigured = true)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_input");
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var videoCredentials = new Mock<IAiVideoCredentialResolver>();
        videoCredentials.Setup(x => x.ResolveAsync(WorkflowTestData.Owner, AiVideoModel.ProviderKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemKeyConfigured ? "system-minimax-key" : null);
        var signer = new Mock<IAiShotQuoteSigner>();
        var context = new Context(fixture, run, asset, videoCredentials, signer);
        signer.Setup(x => x.Sign(It.IsAny<AiShotQuoteClaims>()))
            .Callback<AiShotQuoteClaims>(claims => context.Claims = claims).Returns("signed-quote");
        signer.Setup(x => x.Read("signed-quote")).Returns(() => context.Claims);
        context.Service = new AiShotQuoteService(user.Object, fixture.Runs.Object, fixture.Nodes.Object, fixture.Jobs.Object,
            videoCredentials.Object, fixture.Runtime, signer.Object, fixture.Time);
        return context;
    }

    private sealed class Context(WorkflowTestData fixture, WorkflowRun run, MockupImage asset,
        Mock<IAiVideoCredentialResolver> videoCredentials, Mock<IAiShotQuoteSigner> signer)
    {
        public WorkflowTestData Fixture { get; } = fixture;
        public WorkflowRun Run { get; } = run;
        public MockupImage Asset { get; } = asset;
        public Mock<IAiVideoCredentialResolver> VideoCredentials { get; } = videoCredentials;
        public Mock<IAiShotQuoteSigner> Signer { get; } = signer;
        public AiShotQuoteClaims? Claims { get; set; }
        public AiShotQuoteService Service { get; set; } = null!;
    }
}
