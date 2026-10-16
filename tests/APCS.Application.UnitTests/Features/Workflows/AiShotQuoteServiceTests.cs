using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiShotQuoteServiceTests
{
    [TestMethod]
    [DataRow("single_scene", 12, 1, 0.30)]
    [DataRow("full_video", 8, 1, 0.40)]
    [DataRow("full_video", 15, 2, 0.80)]
    public async Task QuoteAsync_MissingAiClips_ReturnsExactEstimatedCost(string coverage, int duration, int clips, double cost)
    {
        var context = CreateContext();
        var config = Config(context.Asset, coverage, duration);
        using var cancellation = new CancellationTokenSource();

        var result = await context.Service.QuoteAsync(context.Run.Id, new(config), cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.NewClips.Should().Be(clips);
        result.Value.EstimatedCostUsd.Should().Be((decimal)cost);
        result.Value.Token.Should().Be("signed-quote");
        result.Value.ExpiresAtUtc.Should().Be(context.Fixture.Time.GetUtcNow().AddMinutes(10));
        context.Claims.Should().Match<AiShotQuoteClaims>(x => x.RunId == context.Run.Id &&
            x.EstimatedCostUsd == (decimal)cost && !x.Regenerate && x.StoryboardFingerprint.Length == 64);
        context.Keys.Verify(x => x.ListOwnedAsync(WorkflowTestData.Owner, cancellation.Token), Times.Once);
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
        regenerate.Value.EstimatedCostUsd.Should().Be(.30m);
    }

    [TestMethod]
    public async Task QuoteAsync_MissingPaidGeminiKey_RejectsBeforeSigning()
    {
        var context = CreateContext(connectedKey: false);
        var result = await context.Service.QuoteAsync(context.Run.Id, new(Config(context.Asset)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Gemini API key");
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_ForeignRun_DoesNotReadOwnerKeyOrSources()
    {
        var context = CreateContext();
        context.Run.UserId = Guid.NewGuid();

        var result = await context.Service.QuoteAsync(context.Run.Id, new(Config(context.Asset)), default);

        result.Error.Code.Should().Be("RunNotFound");
        context.Keys.Verify(x => x.ListOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
        context.Keys.Verify(x => x.ListOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Signer.Verify(x => x.Sign(It.IsAny<AiShotQuoteClaims>()), Times.Never);
    }

    [TestMethod]
    public async Task VerifyAsync_ExpiredQuote_RejectsWithoutReplanning()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();
        context.Fixture.Time.Advance(TimeSpan.FromMinutes(11));

        var result = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Confirm");
    }

    [TestMethod]
    public async Task VerifyAsync_MissingOrDifferentRunToken_RejectsConfirmation()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();

        var missing = await context.Service.VerifyAsync(context.Run, config, null, false, default);
        context.Claims = context.Claims! with { RunId = Guid.NewGuid() };
        var foreignToken = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, default);

        missing.IsFailure.Should().BeTrue();
        foreignToken.IsFailure.Should().BeTrue();
        missing.Error.Message.Should().Contain("Confirm");
        foreignToken.Error.Message.Should().Contain("Confirm");
    }

    [TestMethod]
    public async Task VerifyAsync_SourceHashOrRegenerationChoiceChanged_RejectsStaleQuote()
    {
        var context = CreateContext();
        var config = Config(context.Asset);
        (await context.Service.QuoteAsync(context.Run.Id, new(config), default)).IsSuccess.Should().BeTrue();

        var wrongChoice = await context.Service.VerifyAsync(context.Run, config, "signed-quote", true, default);
        context.Asset.ContentHash = new string('b', 64);
        context.Asset.MetadataRevision++;
        context.Asset.ApprovedRevision++;
        var changedSource = await context.Service.VerifyAsync(context.Run, config, "signed-quote", false, default);

        wrongChoice.IsFailure.Should().BeTrue();
        changedSource.Error.Code.Should().Be("AiShotQuoteChanged");
    }

    private static GenerateVideoConfig Config(MockupImage source, string coverage = "single_scene", int duration = 12) =>
        new(Mode: "ai_shot", DurationSeconds: duration, AiShotOptions: new(Coverage: coverage, MockupId: source.Id));

    private static Context CreateContext(bool connectedKey = true)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_input");
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var keys = new Mock<IApiKeyRepository>();
        keys.Setup(x => x.ListOwnedAsync(WorkflowTestData.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectedKey ? [new ApiKey
            {
                Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, ServiceProvider = "gemini",
                KeyIdentifier = "Connected Gemini", KeyValueEncrypted = "synthetic-encrypted-value", AuthType = "api_key",
                IsActive = true, LastCheckSucceeded = true
            }] : []);
        var signer = new Mock<IAiShotQuoteSigner>();
        var context = new Context(fixture, run, asset, keys, signer);
        signer.Setup(x => x.Sign(It.IsAny<AiShotQuoteClaims>()))
            .Callback<AiShotQuoteClaims>(claims => context.Claims = claims).Returns("signed-quote");
        signer.Setup(x => x.Read("signed-quote")).Returns(() => context.Claims);
        context.Service = new AiShotQuoteService(user.Object, fixture.Runs.Object, fixture.Nodes.Object, fixture.Jobs.Object,
            keys.Object, fixture.Runtime, signer.Object, fixture.Time);
        return context;
    }

    private sealed class Context(WorkflowTestData fixture, WorkflowRun run, MockupImage asset,
        Mock<IApiKeyRepository> keys, Mock<IAiShotQuoteSigner> signer)
    {
        public WorkflowTestData Fixture { get; } = fixture;
        public WorkflowRun Run { get; } = run;
        public MockupImage Asset { get; } = asset;
        public Mock<IApiKeyRepository> Keys { get; } = keys;
        public Mock<IAiShotQuoteSigner> Signer { get; } = signer;
        public AiShotQuoteClaims? Claims { get; set; }
        public AiShotQuoteService Service { get; set; } = null!;
    }
}
