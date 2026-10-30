using System.Linq.Expressions;
using System.Text.Json;
using APCS.Api.Controllers;
using APCS.Api.UnitTests.TestSupport;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class VideoMediaControllerTests
{
    [TestMethod]
    public async Task Music_WithoutRepository_ReturnsAnEmptyCatalog()
    {
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(),
            Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());

        var result = (await controller.Music(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;

        result.Value.Should().BeAssignableTo<IEnumerable<object>>().Which.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Music_AvailableLicensedTracks_ReturnsSortedCatalogAndFiltersInvalidTracks()
    {
        using var cancellation = new CancellationTokenSource();
        Expression<Func<MusicTrack, bool>>? capturedPredicate = null;
        var repository = new Mock<IRepository<MusicTrack>>(MockBehavior.Strict);
        IReadOnlyList<MusicTrack> tracks =
        [
            Track("Worry", previewUrl: null),
            Track("Ambient Air", previewUrl: "https://media.example/ambient-preview.mp3"),
        ];
        repository.Setup(x => x.FindAsync(It.IsAny<Expression<Func<MusicTrack, bool>>>(), cancellation.Token))
            .Callback((Expression<Func<MusicTrack, bool>> predicate, CancellationToken _) => capturedPredicate = predicate)
            .ReturnsAsync(tracks);
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(),
            Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>(), repository.Object);

        var result = (await controller.Music(cancellation.Token)).Should().BeOfType<OkObjectResult>().Subject;

        var catalog = JsonSerializer.SerializeToElement(result.Value);
        catalog.GetArrayLength().Should().Be(2);
        catalog[0].GetProperty("Name").GetString().Should().Be("Ambient Air");
        catalog[0].GetProperty("PreviewUrl").GetString().Should().Be("https://media.example/ambient-preview.mp3");
        catalog[1].GetProperty("Name").GetString().Should().Be("Worry");
        catalog[1].GetProperty("PreviewUrl").GetString().Should().Be("https://media.example/worry.mp3");

        capturedPredicate.Should().NotBeNull();
        var accepts = capturedPredicate!.Compile();
        accepts(Track("Valid")).Should().BeTrue();
        new[]
        {
            Track("Deleted", deletedAt: DateTime.UtcNow),
            Track("Unavailable", isAvailable: false),
            Track("Not royalty free", royaltyFree: false),
            Track("No license", licenseType: null),
            Track("Blank license", licenseType: ""),
            Track("No audio", audioUrl: null),
            Track("Blank audio", audioUrl: ""),
        }.Should().OnlyContain(track => !accepts(track));
        repository.VerifyAll();
    }

    [TestMethod]
    public async Task Templates_ActiveCatalog_ForwardsCancellationAndReturnsOk()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<VideoTemplateCatalogItem> templates = [new("product_showcase", 2, "Product Showcase",
            "Balanced product video", "/preview.mp4", 12, "fade", "varied", new(1, false, 0))];
        var planning = new Mock<IVideoPlanningService>(MockBehavior.Strict);
        planning.Setup(x => x.ListTemplatesAsync(cancellation.Token)).ReturnsAsync(Result.Success(templates));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), planning.Object, Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());

        var result = await controller.Templates(cancellation.Token);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(templates);
        planning.VerifyAll();
    }

    [TestMethod]
    public async Task Storyboard_ValidPreview_ForwardsProductConfigurationAndCancellation()
    {
        var productId = Guid.NewGuid(); var request = new PreviewStoryboardRequest(new(), [], null); var board = VideoWorkflowTestData.Board();
        using var cancellation = new CancellationTokenSource();
        var planning = new Mock<IVideoPlanningService>(MockBehavior.Strict);
        planning.Setup(x => x.PreviewAsync(productId, request, cancellation.Token)).ReturnsAsync(Result.Success(board));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), planning.Object, Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        var result = await controller.Storyboard(productId, request, cancellation.Token);
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(board);
        planning.VerifyAll();
    }

    [TestMethod]
    public async Task Storyboard_DisabledAi_ReturnsBadRequest()
    {
        var planning = new Mock<IVideoPlanningService>();
        planning.Setup(x => x.PreviewAsync(It.IsAny<Guid>(), It.IsAny<PreviewStoryboardRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Storyboard>(new("UnsupportedVideoMode", "Only Standard is enabled.", ErrorType.Validation)));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), planning.Object, Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        var result = (await controller.Storyboard(Guid.NewGuid(), new(new(Mode: "ai_shot"), [], null), default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(400);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("UnsupportedVideoMode");
    }

    [TestMethod]
    public async Task Upload_ValidFile_ReturnsCreatedAndForwardsStreamNameSizeAndCancellation()
    {
        var productId = Guid.NewGuid(); var response = VideoWorkflowTestData.Mockup(); using var cancellation = new CancellationTokenSource();
        using var content = new MemoryStream([1, 2, 3, 4]);
        var file = new FormFile(content, 0, 4, "file", "source.png");
        var service = new Mock<IMockupAssetService>(MockBehavior.Strict);
        byte[]? received = null;
        service.Setup(x => x.UploadAsync(productId, It.IsAny<Stream>(), "source.png", 4, cancellation.Token))
            .Callback((Guid _, Stream stream, string _, long _, CancellationToken _) =>
            {
                using var buffer = new MemoryStream(); stream.CopyTo(buffer); received = buffer.ToArray();
            }).ReturnsAsync(Result.Success(response));
        var controller = new VideoMediaController(service.Object, Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        var result = (await controller.Upload(productId, file, cancellation.Token)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(201);
        result.Value.Should().BeSameAs(response);
        received.Should().Equal(1, 2, 3, 4);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Upload_InvalidImage_ReturnsBadRequestWithoutCreatedResponse()
    {
        using var content = new MemoryStream([1, 2]);
        var file = new FormFile(content, 0, 2, "file", "invalid.png");
        var service = new Mock<IMockupAssetService>();
        service.Setup(x => x.UploadAsync(It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupResponse>(Error.Validation("Use a static image.")));
        var controller = new VideoMediaController(service.Object, Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        var result = (await controller.Upload(Guid.NewGuid(), file, default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(400);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Use a static image.");
    }

    [TestMethod]
    public async Task GenerateLifestyle_ValidPhoto_ReturnsCreatedAndForwardsUploadDetails()
    {
        var productId = Guid.NewGuid();
        var garmentId = Guid.NewGuid();
        var generated = VideoWorkflowTestData.Mockup();
        using var cancellation = new CancellationTokenSource();
        using var content = new MemoryStream([1, 2, 3]);
        var file = new FormFile(content, 0, 3, "file", "person.png");
        var service = new Mock<ILifestyleMockupService>(MockBehavior.Strict);
        service.Setup(x => x.GenerateAsync(productId, garmentId, It.IsAny<Stream>(), 3, cancellation.Token))
            .ReturnsAsync(Result.Success(generated));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(),
            Mock.Of<IVideoPlanningService>(), service.Object, Mock.Of<IAiStudioFrameService>());

        var result = (await controller.GenerateLifestyle(productId, garmentId, file, cancellation.Token))
            .Should().BeOfType<ObjectResult>().Subject;

        result.StatusCode.Should().Be(StatusCodes.Status201Created);
        result.Value.Should().BeSameAs(generated);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task GenerateLifestyle_ServiceRejectsPhoto_ReturnsValidationProblem()
    {
        using var content = new MemoryStream([1]);
        var file = new FormFile(content, 0, 1, "file", "person.gif");
        var service = new Mock<ILifestyleMockupService>();
        service.Setup(x => x.GenerateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupResponse>(Error.Validation("Use a static image.")));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(),
            Mock.Of<IVideoPlanningService>(), service.Object, Mock.Of<IAiStudioFrameService>());

        var result = (await controller.GenerateLifestyle(Guid.NewGuid(), Guid.NewGuid(), file, CancellationToken.None))
            .Should().BeOfType<ObjectResult>().Subject;

        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Use a static image.");
    }

    [TestMethod]
    public async Task AssetReadUpdateAndReview_ForwardRevisionRequestsAndCancellation()
    {
        var asset = VideoWorkflowTestData.Mockup(); using var cancellation = new CancellationTokenSource();
        var metadata = new MockupMetadataRequest(2, "Hero", "artwork", "red", new());
        var review = new MockupReviewRequest(2, true);
        var service = new Mock<IMockupAssetService>(MockBehavior.Strict);
        service.Setup(x => x.ListAsync(asset.ProductId, cancellation.Token)).ReturnsAsync(Result.Success<IReadOnlyList<MockupResponse>>([asset]));
        service.Setup(x => x.UpdateAsync(asset.Id, metadata, cancellation.Token)).ReturnsAsync(Result.Success(asset));
        service.Setup(x => x.ReviewAsync(asset.Id, review, cancellation.Token)).ReturnsAsync(Result.Success(asset));
        var controller = new VideoMediaController(service.Object, Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        (await controller.List(asset.ProductId, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<IReadOnlyList<MockupResponse>>().Which.Should().ContainSingle(x => x.Id == asset.Id);
        (await controller.Update(asset.Id, metadata, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(asset);
        (await controller.Review(asset.Id, review, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(asset);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task AiTemplates_ForwardsCancellationAndReturnsOk()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<AiVideoTemplateDefinition> templates = [];
        var planning = new Mock<IVideoPlanningService>(MockBehavior.Strict);
        planning.Setup(x => x.ListAiTemplatesAsync(cancellation.Token)).ReturnsAsync(Result.Success(templates));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), planning.Object, Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());

        (await controller.AiTemplates(cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(templates);
        planning.VerifyAll();
    }

    [TestMethod]
    public async Task StudioFrame_QuoteIsOkAndANewFrameIsCreated()
    {
        var productId = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        var request = new AiStudioFrameRequest(Guid.NewGuid(), "centered", "9:16");
        var quote = new AiStudioFrameQuote("fingerprint", 1, 0.04m, null, "note");
        var generated = new AiStudioFrameGenerationResponse(VideoWorkflowTestData.Mockup(), false, 0.04m);
        var frames = new Mock<IAiStudioFrameService>(MockBehavior.Strict);
        frames.Setup(x => x.QuoteAsync(productId, request, cancellation.Token)).ReturnsAsync(Result.Success(quote));
        frames.Setup(x => x.GenerateAsync(productId, request, cancellation.Token)).ReturnsAsync(Result.Success(generated));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), frames.Object);

        (await controller.QuoteStudioFrame(productId, request, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(quote);
        var created = (await controller.GenerateStudioFrame(productId, request, cancellation.Token)).Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeSameAs(generated);
        frames.VerifyAll();
    }

    [TestMethod]
    public async Task GenerateStudioFrame_RefusedRequest_ReturnsItsProblem()
    {
        var frames = new Mock<IAiStudioFrameService>();
        frames.Setup(x => x.GenerateAsync(It.IsAny<Guid>(), It.IsAny<AiStudioFrameRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AiStudioFrameGenerationResponse>(Error.NotFound("MockupNotFound", "Product or mockup not found.")));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), Mock.Of<IVideoArtifactService>(), Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), frames.Object);

        var result = (await controller.GenerateStudioFrame(Guid.NewGuid(), new(Guid.NewGuid(), "centered", "9:16"), default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task DownloadAndZip_UseCorrectResourceKindAndCancellation()
    {
        var videoId = Guid.NewGuid(); var zipId = Guid.NewGuid(); using var cancellation = new CancellationTokenSource();
        var service = new Mock<IVideoArtifactService>(MockBehavior.Strict);
        service.Setup(x => x.DownloadAsync(videoId, false, cancellation.Token)).ReturnsAsync(Result.Success("https://example.invalid/video"));
        service.Setup(x => x.DownloadAsync(zipId, true, cancellation.Token)).ReturnsAsync(Result.Success("https://example.invalid/zip"));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), service.Object, Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        (await controller.Download(videoId, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be("https://example.invalid/video");
        (await controller.DownloadZip(zipId, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be("https://example.invalid/zip");
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Download_ForeignOrExpiredMedia_ReturnsNotFoundProblem()
    {
        var service = new Mock<IVideoArtifactService>();
        service.Setup(x => x.DownloadAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<string>(Error.NotFound("MediaNotFound", "Ready media not found.")));
        var controller = new VideoMediaController(Mock.Of<IMockupAssetService>(), service.Object, Mock.Of<IVideoPlanningService>(), Mock.Of<ILifestyleMockupService>(), Mock.Of<IAiStudioFrameService>());
        var result = (await controller.Download(Guid.NewGuid(), default)).Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(404);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("MediaNotFound");
    }

    [TestMethod]
    [DataRow(typeof(VideoMediaController))]
    [DataRow(typeof(WorkflowsController))]
    [DataRow(typeof(WorkflowRunsController))]
    public void PublicWorkflowControllers_RequireSellerAuthorization(Type controller)
    {
        controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles.Should().Be("Seller");
        controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }

    private static MusicTrack Track(
        string name,
        DateTime? deletedAt = null,
        bool? isAvailable = true,
        bool royaltyFree = true,
        string? licenseType = "owned",
        string? audioUrl = "https://media.example/worry.mp3",
        string? previewUrl = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ArtistName = "LONOWN",
            DurationSeconds = 28,
            Genre = "Other",
            Mood = "Calm",
            DeletedAt = deletedAt,
            IsAvailable = isAvailable,
            RoyaltyFree = royaltyFree,
            LicenseType = licenseType!,
            AudioUrl = audioUrl!,
            PreviewUrl = previewUrl,
        };
}
