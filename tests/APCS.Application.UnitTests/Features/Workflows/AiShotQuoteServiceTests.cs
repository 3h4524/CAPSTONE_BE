using APCS.Application.Abstractions.AI;
using APCS.Application.UnitTests.TestSupport;
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
    [DataRow(true)]
    [DataRow(false)]
    public async Task QuoteAsync_CancelledRunSceneRegeneration_QuotesOnlyTargetAndRequiresOtherStoredClip(bool otherStored)
    {
        var context = CreateContext();
        context.Run.Status = "cancelled";
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 8,
            AiScenes: [new(MockupId: context.Asset.Id, DurationSeconds: 4, RightsConfirmed: true),
                new(MockupId: context.Asset.Id, DurationSeconds: 4, RightsConfirmed: true)]);
        var board = (await context.Fixture.Runtime.PlanAsync(context.Run, config, default)).Value;
        var job = context.Fixture.AddJob(context.Run);
        context.Fixture.NodeRows.Single(item => item.NodeType == "generate-video").Status = "succeeded";
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), board, board.AiShots!.Select(shot =>
            new AiShotJobState(shot.SceneOrder, shot.Fingerprint,
                StorageKey: shot.SceneOrder == 1 && !otherStored ? null : $"private/shot-{shot.SceneOrder}.mp4",
                StorageVersion: shot.SceneOrder == 1 && !otherStored ? null : "1",
                Status: shot.SceneOrder == 1 && !otherStored ? "pending" : "stored")).ToArray()));

        var result = await context.Service.QuoteAsync(context.Run.Id, new(config, Regenerate: true, SceneOrder: 0), default);

        if (!otherStored)
        {
            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("OtherScenesIncomplete");
            context.Signer.Verify(item => item.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
            return;
        }
        result.IsSuccess.Should().BeTrue();
        result.Value.NewClips.Should().Be(1);
        context.Claims!.SceneOrder.Should().Be(0);
        context.Claims.Regenerate.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task QuoteAsync_CancelledRunWithoutCompletedTargetScene_RejectsQuote(bool regenerate, bool selectScene)
    {
        var context = CreateContext();
        context.Run.Status = "cancelled";
        context.Fixture.NodeRows.Single(item => item.NodeType == "generate-video").Status = "cancelled";

        var result = await context.Service.QuoteAsync(context.Run.Id,
            new(Config(context.Asset), Regenerate: regenerate, SceneOrder: selectScene ? 0 : null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("InvalidCheckpoint");
        context.Signer.Verify(item => item.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
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

    [TestMethod]
    public async Task QuoteBatchAsync_SeveralDesigns_AddsUpEveryClipAndBindsTheTokenToTheGroup()
    {
        var context = CreateContext();
        var groupId = Guid.NewGuid();
        var plans = BatchPlans(context);

        var result = await context.Service.QuoteBatchAsync(groupId, plans, default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.NewClips.Should().Be(2);
        result.Value.EstimatedCostUsd.Should().Be(.96m);
        result.Value.Note.Should().Contain("2 videos");
        context.Claims.Should().Match<AiShotQuoteClaims>(x => x.RunId == groupId && x.EstimatedCostUsd == .96m
            && !x.Regenerate && x.SceneOrder == null && x.ConfigFingerprint != null);
    }

    [TestMethod]
    public async Task QuoteBatchAsync_StandardVideosOnly_HasNothingToQuote()
    {
        var context = CreateContext();
        var standard = new PlannedVideo(context.Run, new GenerateVideoConfig(), "Tee · Design 1", "sources");

        var result = await context.Service.QuoteBatchAsync(Guid.NewGuid(), [standard], default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("No design of this run makes an AI video clip");
    }

    [TestMethod]
    public async Task VerifyBatchAsync_TheQuotedDesigns_PassOnlyWithTheirOwnToken()
    {
        var context = CreateContext();
        var groupId = Guid.NewGuid();
        var plans = BatchPlans(context);
        await context.Service.QuoteBatchAsync(groupId, plans, default);

        var confirmed = await context.Service.VerifyBatchAsync(groupId, plans, "signed-quote", default);
        var missing = await context.Service.VerifyBatchAsync(groupId, plans, null, default);
        var otherGroup = await context.Service.VerifyBatchAsync(Guid.NewGuid(), plans, "signed-quote", default);
        var changedImage = await context.Service.VerifyBatchAsync(groupId,
            [plans[0] with { SourceFingerprint = "other" }, plans[1]], "signed-quote", default);

        confirmed.IsSuccess.Should().BeTrue();
        missing.IsFailure.Should().BeTrue();
        otherGroup.IsFailure.Should().BeTrue();
        changedImage.Error.Code.Should().Be("AiShotQuoteChanged");
    }

    [TestMethod]
    public async Task VerifyBatchAsync_StandardVideosOnly_NeedsNoConfirmedCost()
    {
        var context = CreateContext();
        var standard = new PlannedVideo(context.Run, new GenerateVideoConfig(), "Tee · Design 1", "sources");

        var result = await context.Service.VerifyBatchAsync(Guid.NewGuid(), [standard], null, default);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>Two designs of a batch run: the preview product's, and one whose run does not exist yet.</summary>
    private static PlannedVideo[] BatchPlans(Context context)
    {
        var other = context.Fixture.AddProduct();
        var otherAsset = context.Fixture.AddAsset(other);
        var planned = new WorkflowRun
        {
            Id = Guid.NewGuid(), WorkflowId = context.Run.WorkflowId, ProductId = other.Id, UserId = WorkflowTestData.Owner,
            DefinitionSnapshot = context.Run.DefinitionSnapshot, WorkflowRevision = 4, Revision = 1, Status = "queued",
            IdempotencyKey = "group:1", RequestHash = "group", CreatedAt = WorkflowTestData.Now.UtcDateTime,
            UpdatedAt = WorkflowTestData.Now.UtcDateTime
        };
        return
        [
            new(context.Run, Config(context.Asset), "Tee · Design 1", "sources-1"),
            new(planned, Config(otherAsset), "Mug · Design 1", "sources-2"),
        ];
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
            videoCredentials.Object, fixture.Runtime, signer.Object, fixture.Time, TestEntitlements.AllowAll());
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
