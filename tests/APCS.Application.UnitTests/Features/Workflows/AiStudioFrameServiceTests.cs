using System.Linq.Expressions;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiStudioFrameServiceTests
{
    [TestMethod]
    public async Task QuoteAsync_MatchingApprovedFrame_ReturnsReusableAssetWithoutCost()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var frame = AddFrame(context, source, "approved");

        var result = await context.Service.QuoteAsync(context.Product.Id, Request(source), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExistingFrame!.Id.Should().Be(frame.Id);
        result.Value.EstimatedCostUsd.Should().Be(0m);
        result.Value.Note.Should().Contain("reused without another image generation call");
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task QuoteAsync_PendingMatchingFrame_ReturnsReviewCandidateWithoutCallingImageProvider()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var frame = AddFrame(context, source, "pending");

        var result = await context.Service.QuoteAsync(context.Product.Id, Request(source), default);

        result.Value.ExistingFrame!.Id.Should().Be(frame.Id);
        result.Value.ExistingFrame.ApprovalStatus.Should().Be("pending");
        result.Value.EstimatedCostUsd.Should().Be(0m);
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("rejected")]
    [DataRow("stale_source")]
    [DataRow("wrong_layout")]
    [DataRow("wrong_aspect")]
    public async Task QuoteAsync_RejectedStaleOrMismatchedFrame_ReturnsNewImageEstimate(string state)
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        AddFrame(context, source, state is "wrong_layout" or "wrong_aspect" ? "approved" : state,
            state == "wrong_layout" ? AiStudioFrameIdentity.FlatLay : AiStudioFrameIdentity.HangingWindow,
            state == "wrong_aspect" ? "16:9" : "9:16");
        if (state == "stale_source")
        {
            source.MetadataRevision++;
            source.ApprovedRevision++;
        }

        var result = await context.Service.QuoteAsync(context.Product.Id, Request(source), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExistingFrame.Should().BeNull();
        result.Value.EstimatedCostUsd.Should().Be(0.05m);
    }

    [TestMethod]
    [DataRow("fingerprint")]
    [DataRow("estimate")]
    [DataRow("confirmation")]
    public async Task GenerateAsync_QuoteMismatch_DoesNotCallImageProviderOrStoreUsage(string mismatch)
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var quote = (await context.Service.QuoteAsync(context.Product.Id, Request(source), default)).Value;
        var request = Request(source) with
        {
            ExpectedFingerprint = mismatch == "fingerprint" ? "stale-fingerprint" : quote.Fingerprint,
            ConfirmedEstimatedCostUsd = mismatch == "confirmation" ? null : mismatch == "estimate" ? 0.04m : quote.EstimatedCostUsd
        };

        var result = await context.Service.GenerateAsync(context.Product.Id, request, default);

        result.Error.Code.Should().Be("StudioFrameQuoteChanged");
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UsageRows.Should().BeEmpty();
        context.Fixture.AssetRows.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GenerateAsync_ConfirmedQuote_CreatesPendingAiFrameAndUsageRecord()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var quote = (await context.Service.QuoteAsync(context.Product.Id, Request(source), default)).Value;
        var request = Request(source) with { ExpectedFingerprint = quote.Fingerprint, ConfirmedEstimatedCostUsd = quote.EstimatedCostUsd };

        var result = await context.Service.GenerateAsync(context.Product.Id, request, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reused.Should().BeFalse();
        result.Value.EstimatedCostUsd.Should().Be(0.05m);
        result.Value.Frame.SourceType.Should().Be(MockupSources.AiGenerated);
        result.Value.Frame.Role.Should().Be("VideoScene");
        result.Value.Frame.ApprovalStatus.Should().Be("pending");
        var saved = context.Fixture.AssetRows.Single(asset => asset.Id == result.Value.Frame.Id);
        saved.ApprovedRevision.Should().BeNull();
        AiStudioFrameIdentity.IsCurrent(saved, source, AiStudioFrameIdentity.HangingWindow, "9:16").Should().BeTrue();
        context.UsageRows.Should().ContainSingle().Which.CostUsd.Should().Be(0.05m);
        // No preset staging: without a direction from the user, the product keeps the arrangement of its source image.
        context.Images.Verify(x => x.ComposeAsync("decrypted-gemini-key", It.Is<string>(prompt =>
                prompt.Contains("Follow the user's additional visual direction") && prompt.Contains("preserve the product's natural arrangement from the source image")),
            It.Is<IReadOnlyList<ImageReference>>(images => images.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
        context.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_MatchingApprovedFrame_ReturnsItWithoutNewChargeOrImageCall()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var frame = AddFrame(context, source, "approved");

        var result = await context.Service.GenerateAsync(context.Product.Id, Request(source), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Frame.Id.Should().Be(frame.Id);
        result.Value.Reused.Should().BeTrue();
        result.Value.EstimatedCostUsd.Should().Be(0m);
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UsageRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GenerateAsync_RegenerateBypassesMatchingFrameAndCreatesNewPendingRevision()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        var existing = AddFrame(context, source, "approved");
        var regenerateRequest = Request(source) with { Regenerate = true };
        var quote = (await context.Service.QuoteAsync(context.Product.Id, regenerateRequest, default)).Value;
        var confirmed = regenerateRequest with { ExpectedFingerprint = quote.Fingerprint, ConfirmedEstimatedCostUsd = quote.EstimatedCostUsd };

        var result = await context.Service.GenerateAsync(context.Product.Id, confirmed, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reused.Should().BeFalse();
        result.Value.Frame.Id.Should().NotBe(existing.Id);
        result.Value.Frame.ApprovalStatus.Should().Be("pending");
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_ProductOtherThanATShirt_IsNamedAsThatProduct()
    {
        var context = CreateContext("mug");
        var source = context.Fixture.AddAsset(context.Product);
        var request = new AiStudioFrameRequest(source.Id, AiStudioFrameIdentity.FlatLay, VideoOutputFormats.Default);
        var quote = (await context.Service.QuoteAsync(context.Product.Id, request, default)).Value;

        var result = await context.Service.GenerateAsync(context.Product.Id,
            request with { ExpectedFingerprint = quote.Fingerprint, ConfirmedEstimatedCostUsd = quote.EstimatedCostUsd }, default);

        result.IsSuccess.Should().BeTrue();
        context.Images.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.Is<string>(prompt => prompt.Contains("identity reference for the mug") && !prompt.Contains("T-shirt")),
            It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task QuoteAsync_SourceThatIsNotACurrentApprovedProductImage_IsRefusedBeforeLookingForFrames()
    {
        var context = CreateContext();
        var source = context.Fixture.AddAsset(context.Product);
        source.ApprovalStatus = "pending";

        var result = await context.Service.QuoteAsync(context.Product.Id, Request(source), default);

        result.Error.Code.Should().Be("SourceNotApproved");
        context.Fixture.Assets.Verify(x => x.FindAsync(It.IsAny<Expression<Func<MockupImage, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AiStudioFrameRequest Request(MockupImage source) =>
        new(source.Id, AiStudioFrameIdentity.HangingWindow, VideoOutputFormats.Default);

    private static MockupImage AddFrame(Context context, MockupImage source, string state, string layout = AiStudioFrameIdentity.HangingWindow, string? aspectRatio = null)
    {
        var frame = WorkflowTestData.Asset(source.ProductId, "VideoScene");
        frame.SourceType = MockupSources.AiGenerated;
        frame.MetadataRevision = 1;
        frame.ApprovedRevision = state == "approved" ? 1 : null;
        frame.ApprovalStatus = state;
        frame.ContentHash = new string('b', 64);
        frame.CreatedAt = WorkflowTestData.Now.UtcDateTime;
        aspectRatio ??= VideoOutputFormats.Resolve(VideoOutputFormats.Default).AspectRatio;
        frame.Regions = AiStudioFrameIdentity.SerializeRegions(new(
            AiStudioFrameIdentity.Fingerprint(source, layout, aspectRatio), source.Id, source.ContentHash!, source.MetadataRevision,
            layout, aspectRatio, AiStudioFrameIdentity.PromptVersion));
        context.Fixture.AssetRows.Add(frame);
        return frame;
    }

    private static Context CreateContext(string productType = "tshirt")
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct(productType);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var apiKeys = new Mock<IApiKeyRepository>();
        apiKeys.Setup(x => x.ListOwnedAsync(WorkflowTestData.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiKey
            {
                Id = Guid.NewGuid(), UserId = WorkflowTestData.Owner, ServiceProvider = "gemini",
                KeyValueEncrypted = "encrypted-gemini-key", IsActive = true, LastCheckSucceeded = true
            }]);
        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect("encrypted-gemini-key")).Returns("decrypted-gemini-key");
        var images = new Mock<IImageGenerationProvider>();
        images.SetupGet(x => x.EstimatedCostPerImageUsd).Returns(0.05m);
        images.Setup(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success([0xff, 0xd8, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0], "image/jpeg", "gemini-image-test", 0.05m));
        var storage = new Mock<IMediaStorage>();
        storage.Setup(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync([0xff, 0xd8, 0xff]);
        storage.Setup(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredMockup("private/studio-frame", "2", new string('c', 64), 1080, 1920));
        storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns((string key, string _, string? _, bool _) => $"https://media.test/{key}");
        var mockupService = new Mock<IMockupAssetService>();
        mockupService.Setup(x => x.ListAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Success<IReadOnlyList<MockupResponse>>(fixture.AssetRows.Select(asset => new MockupResponse(
                asset.Id, asset.ProductId, asset.SourceType, asset.Role, asset.ArtworkGroupKey, asset.VariantKey,
                asset.MetadataRevision, asset.ApprovalStatus, asset.ApprovedRevision, asset.MockupWidthPx, asset.MockupHeightPx,
                new MockupRegions(), $"https://media.test/{asset.Id}", [])).ToArray()));
        var usageRows = new List<ApiUsageRecord>();
        var usage = new Mock<IRepository<ApiUsageRecord>>();
        usage.Setup(x => x.AddAsync(It.IsAny<ApiUsageRecord>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((ApiUsageRecord row, bool _, CancellationToken _) => usageRows.Add(row)).Returns(Task.CompletedTask);
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new AiStudioFrameService(user.Object, fixture.Products.Object, fixture.Assets.Object, usage.Object,
            apiKeys.Object, credentials.Object, images.Object, storage.Object, mockupService.Object, unit.Object,
            fixture.Time, NullLogger<AiStudioFrameService>.Instance);

        return new Context(fixture, product, service, images, unit, usageRows);
    }

    private sealed record Context(WorkflowTestData Fixture, Product Product, AiStudioFrameService Service,
        Mock<IImageGenerationProvider> Images, Mock<IUnitOfWork> Unit, List<ApiUsageRecord> UsageRows);
}
