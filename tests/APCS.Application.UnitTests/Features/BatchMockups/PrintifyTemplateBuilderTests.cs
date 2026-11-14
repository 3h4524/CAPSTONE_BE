using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.BatchMockups.Printify;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class PrintifyTemplateBuilderTests
{
    private const long Blueprint = 1319;
    private const long Provider = 99;
    private const long Shop = 500;

    // Bella+Canvas 3001U by this provider prints the front and back only.
    private static readonly PrintifyCatalogVariant[] Variants =
    [
        new(1, "White / S", "White", "S", [new("front", 3600, 4500), new("back", 3600, 4500)]),
        new(2, "White / M", "White", "M", [new("front", 3600, 4500), new("back", 3600, 4500)]),
        new(3, "Black / M", "Black", "M", [new("front", 3600, 4500), new("back", 3600, 4500)]),
        new(4, "Navy / M", "Navy", "M", [new("front", 3600, 4500), new("back", 3600, 4500)]),
    ];

    private readonly Guid adminId = Guid.NewGuid();
    private readonly Mock<IPrintifyClient> printify = new();
    private readonly Mock<IPrintifyAdminToken> token = new();
    private readonly Mock<IPrintifyMockupCalibrator> calibrator = new();
    private readonly Mock<IPrintifyTemplateJobQueue> queue = new();
    private readonly TestMemoryCache cache = new();
    private readonly Mock<IPublicImageService> images = new();
    private readonly Mock<IMockupMapService> mapService = new();
    private readonly Mock<IMockupTemplateService> templateService = new();
    private readonly Mock<IRepository<MockupTemplatePrintifyLink>> links = new();
    private readonly Mock<IRepository<PrintifyColorMap>> colors = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly Mock<IUnitOfWorkTransaction> transaction = new();
    private readonly List<MockupTemplatePrintifyLink> addedLinks = [];
    private readonly List<PrintifyColorMap> addedColors = [];

    [TestInitialize]
    public void Setup()
    {
        token.SetupGet(x => x.Token).Returns("platform-token");
        printify.Setup(x => x.GetBlueprintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyBlueprint(Blueprint, "Unisex Heavy Tee", "Bella+Canvas", "3001U", "https://img/b.png")]);
        printify.Setup(x => x.GetBlueprintProvidersAsync(It.IsAny<string>(), Blueprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyProvider(Provider, "Printify Choice", null, null)]);
        printify.Setup(x => x.GetProvidersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyProvider(Provider, "Printify Choice", "US", null)]);
        printify.Setup(x => x.GetVariantsAsync(It.IsAny<string>(), Blueprint, Provider, It.IsAny<CancellationToken>())).ReturnsAsync(Variants);
        printify.Setup(x => x.GetShopsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([new PrintifyShop(Shop, "Store", "etsy")]);
        printify.Setup(x => x.UploadImageContentsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, byte[] _, CancellationToken _) => new PrintifyUploadedImage(name.Contains("calibration") ? "sheet" : "blank", 1200, 1200));
        calibrator.Setup(x => x.Sheets()).Returns(([1], [2]));
        unitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        links.Setup(x => x.AddAsync(It.IsAny<MockupTemplatePrintifyLink>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<MockupTemplatePrintifyLink, bool, CancellationToken>((link, _, _) => addedLinks.Add(link));
        colors.Setup(x => x.Query()).Returns(() => addedColors.AsQueryable().BuildMock());
        colors.Setup(x => x.AddAsync(It.IsAny<PrintifyColorMap>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<PrintifyColorMap, bool, CancellationToken>((row, _, _) => addedColors.Add(row));
    }

    private PrintifyTemplateBuilder Builder(params MockupTemplate[] templates)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(adminId);
        var templateRepository = new Mock<IRepository<MockupTemplate>>();
        templateRepository.Setup(x => x.Query()).Returns(() => templates.AsQueryable().BuildMock());
        var catalog = new PrintifyAdminCatalog(printify.Object, token.Object, cache, NullLogger<PrintifyAdminCatalog>.Instance);
        var colorMap = new PrintifyColorMapService(colors.Object, catalog, token.Object, unitOfWork.Object, TimeProvider.System);
        return new PrintifyTemplateBuilder(current.Object, printify.Object, token.Object, catalog, colorMap, calibrator.Object, queue.Object, cache,
            images.Object, mapService.Object, templateService.Object, templateRepository.Object, links.Object, unitOfWork.Object,
            TimeProvider.System, NullLogger<PrintifyTemplateBuilder>.Instance);
    }

    private static PrintifyProductImage Image(string product, long variant, string camera, params long[] variantIds) =>
        new($"https://images.printify.com/mockup/{product}/{variant}/9{camera.Length}/x.jpg?camera_label={camera}", variantIds, null, false);

    [TestMethod]
    public async Task StartAsync_WithoutTheServerToken_SaysToSetIt()
    {
        token.SetupGet(x => x.Token).Returns((string?)null);

        var result = await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["front"]));

        result.Error.Message.Should().Contain("Printify__CatalogToken");
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_SleeveTheProviderDoesNotPrint_IsRefused()
    {
        var result = await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["front", "left"]));

        result.Error.Message.Should().Be("Printify Choice prints nothing on the left of this product.");
    }

    [TestMethod]
    public async Task StartAsync_WithoutTheFront_IsRefused()
    {
        var result = await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["back"]));

        result.Error.Message.Should().Contain("front");
    }

    [TestMethod]
    public async Task StartAsync_QueuesTheJobWithWhiteMeasuredFirst()
    {
        var result = await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["front", "back"], ["black", "Navy"]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("queued");
        result.Value.Colors.Should().Equal("White", "Black", "Navy");
        queue.Verify(x => x.Enqueue(result.Value.JobId), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_PrintifyFailingHalfway_StillDeletesTheTemporaryProduct()
    {
        var job = (await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["front"]))).Value;
        printify.Setup(x => x.CreateProbeProductAsync(It.IsAny<string>(), Shop, It.Is<PrintifyProbeProduct>(probe => probe.ImageId == "sheet"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintifyProduct("A", "probe", false, null, null, [], [], [Image("A", 2, "front", 2)]));
        printify.Setup(x => x.CreateProbeProductAsync(It.IsAny<string>(), Shop, It.Is<PrintifyProbeProduct>(probe => probe.ImageId == "blank"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PrintifyApiException(PrintifyFailureKind.Unavailable, "down"));

        await Builder().RunAsync(job.JobId);

        var after = (await Builder().GetAsync(job.JobId)).Value;
        after.Status.Should().Be("failed");
        after.Error.Should().Contain("Printify");
        printify.Verify(x => x.DeleteProductAsync(It.IsAny<string>(), Shop, "A", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_FindsThePrintAreaAndMeasuresTheColorsThenDeletesBothProducts()
    {
        var job = (await Builder().StartAsync(new StartPrintifyTemplateJobRequestDto(Blueprint, Provider, ["front"], ["Black"]))).Value;
        printify.Setup(x => x.CreateProbeProductAsync(It.IsAny<string>(), Shop, It.Is<PrintifyProbeProduct>(probe => probe.ImageId == "sheet"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintifyProduct("A", "probe", false, null, null, [], [], [Image("A", 2, "front", 2), Image("A", 2, "person-1", 2)]));
        // The bare product's photos name a sibling variant id; the listed variant tells the color.
        printify.Setup(x => x.CreateProbeProductAsync(It.IsAny<string>(), Shop, It.Is<PrintifyProbeProduct>(probe => probe.ImageId == "blank"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintifyProduct("B", "probe", false, null, null, [], [], [Image("B", 77, "front", 2), Image("B", 78, "front", 3)]));
        calibrator.Setup(x => x.DownloadMockupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, CancellationToken _) => System.Text.Encoding.UTF8.GetBytes(url));
        calibrator.Setup(x => x.DetectPrintArea(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Returns(new PrintAreaDetection(new PrintAreaOnPhoto(680, 600, 690, 862), 2048, 2048, null));
        calibrator.Setup(x => x.MeasureFabric(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<PrintAreaOnPhoto>()))
            .Returns(new FabricMeasurement("#202020", "#232424"));
        images.Setup(x => x.UploadImageWithMetadataAsync(It.IsAny<UploadFileDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicImageUploadResult("https://cdn/probe-front.jpg", 2048, 2048));

        await Builder().RunAsync(job.JobId);

        var after = (await Builder().GetAsync(job.JobId)).Value;
        after.Status.Should().Be("done", after.Error);
        after.Sides.Should().ContainSingle().Which.Candidates.Should().ContainSingle()
            .Which.Should().Be(new PrintifyCameraCandidateDto("front", "https://cdn/probe-front.jpg", 2048, 2048, 680, 600, 690, 862));
        after.MeasuredColors.Should().Equal(new PrintifyMeasuredColorDto("White", "#202020", "#FFFFFF"), new PrintifyMeasuredColorDto("Black", "#202020", "#232424"));
        printify.Verify(x => x.DeleteProductAsync(It.IsAny<string>(), Shop, "A", It.IsAny<CancellationToken>()), Times.Once);
        printify.Verify(x => x.DeleteProductAsync(It.IsAny<string>(), Shop, "B", It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<PrintifyTemplateJobDto> FinishedJobAsync(params string[] views)
    {
        var job = new PrintifyTemplateJobDto(Guid.NewGuid(), "done", null, null, Blueprint, "Unisex Heavy Tee", null, Provider, "Printify Choice", null,
            views, ["White", "Black"],
            views.Select(view => new PrintifySideResultDto(view, view, [new PrintifyCameraCandidateDto(view, $"https://cdn/{view}.jpg", 2048, 2048, 600, 500, 700, 860)], null)).ToList(),
            [new PrintifyMeasuredColorDto("White", "#F0F0F0", "#FFFFFF"), new PrintifyMeasuredColorDto("Black", "#202020", "#232424")],
            [], ["S", "M", "L", "XL"], DateTimeOffset.UtcNow);
        await cache.SetAsync(PrintifyTemplateBuilder.JobKey(job.JobId), job);
        return job;
    }

    private static MockupTemplateResponseDto Response(Guid id) =>
        new(id, "Bella Heavy", "tshirt", "https://cdn/x.jpg", null, "{}", 2048, 2048, 0, true, false, true, true, null, null);

    [TestMethod]
    public async Task CreateAsync_SavesTheTemplateItsSidesLinkAndColorsInOneTransaction()
    {
        var job = await FinishedJobAsync("front", "back");
        var mainId = Guid.NewGuid();
        var requests = new List<CreateMockupTemplateRequestDto>();
        mapService.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        templateService.Setup(x => x.CreateAsync(It.IsAny<CreateMockupTemplateRequestDto>(), true, It.IsAny<CancellationToken>()))
            .Callback<CreateMockupTemplateRequestDto, bool, CancellationToken>((request, _, _) => requests.Add(request))
            .ReturnsAsync((CreateMockupTemplateRequestDto request, bool _, CancellationToken _) =>
                Result.Success(Response(request.MainTemplateId is null ? mainId : Guid.NewGuid())));
        templateService.Setup(x => x.ListSystemAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MockupTemplateResponseDto>>([Response(mainId)]));

        var result = await Builder().CreateAsync(job.JobId, new CreatePrintifyTemplateRequestDto(
            new Dictionary<string, string> { ["front"] = "front", ["back"] = "back" }, ["White", "Black"], "Bella Heavy", "tshirt", ["M", "L", "4XL"]));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        requests.Should().HaveCount(2);
        requests[0].Should().Match<CreateMockupTemplateRequestDto>(request => request.MainTemplateId == null && request.X == 600 && request.Width == 700 && request.AllowRecolor);
        requests[1].Should().Match<CreateMockupTemplateRequestDto>(request => request.MainTemplateId == mainId && request.View == "back" && request.FromPrintify);
        addedLinks.Should().ContainSingle().Which.Should().Match<MockupTemplatePrintifyLink>(link =>
            link.MockupTemplateId == mainId && link.BlueprintId == Blueprint && link.PrintProviderId == Provider && link.UpdatedBy == adminId
            && link.DefaultSizes.SequenceEqual(new[] { "M", "L" }));
        addedColors.Select(row => (row.PrintifyName, row.Hex)).Should().Equal(("White", "#FFFFFF"), ("Black", "#232424"));
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_ASideFailing_RollsBackAndLinksNothing()
    {
        var job = await FinishedJobAsync("front", "back");
        mapService.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        templateService.Setup(x => x.CreateAsync(It.IsAny<CreateMockupTemplateRequestDto>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateMockupTemplateRequestDto request, bool _, CancellationToken _) => request.MainTemplateId is null
                ? Result.Success(Response(Guid.NewGuid()))
                : Result.Failure<MockupTemplateResponseDto>(Error.Validation("too dark")));

        var result = await Builder().CreateAsync(job.JobId, new CreatePrintifyTemplateRequestDto(
            new Dictionary<string, string> { ["front"] = "front", ["back"] = "back" }, ["Black"], "Bella Heavy", "tshirt"));

        result.IsFailure.Should().BeTrue();
        transaction.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        addedLinks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateAsync_CameraThatWasNotMeasured_IsRefused()
    {
        var job = await FinishedJobAsync("front");

        var result = await Builder().CreateAsync(job.JobId, new CreatePrintifyTemplateRequestDto(
            new Dictionary<string, string> { ["front"] = "front-2" }, [], "Bella Heavy", "tshirt"));

        result.Error.Message.Should().Contain("front");
        unitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void CameraOf_ReadsTheCameraLabel()
    {
        PrintifyTemplateBuilder.CameraOf("https://images.printify.com/mockup/p/12/345/x.jpg?camera_label=Front-2&s=400").Should().Be("front-2");
        PrintifyTemplateBuilder.CameraOf("https://images.printify.com/mockup/p/12/345/x.jpg").Should().Be("345");
        PrintifyTemplateBuilder.VariantIdOf("https://images.printify.com/mockup/p/12/345/x.jpg").Should().Be(12);
        PrintifyTemplateBuilder.ViewOfCamera("back-2", null).Should().Be("back");
        PrintifyTemplateBuilder.ViewOfCamera("person-1", "front").Should().Be("front");
        PrintifyTemplateBuilder.ViewOfCamera("folded", null).Should().BeNull();
        PrintifyTemplateBuilder.ViewOfCamera("person-1", "left_sleeve_dtf").Should().Be("left");
    }

    [TestMethod]
    public void PositionIn_FindsTheAreaWithOrWithoutHowItIsPrinted()
    {
        string[] dtf = ["front_dtf", "back_dtf", "left_sleeve_dtf", "right_sleeve_dtf"];
        PrintifyTemplateBuilder.PositionIn("front", dtf).Should().Be("front_dtf");
        PrintifyTemplateBuilder.PositionIn("left", dtf).Should().Be("left_sleeve_dtf");
        PrintifyTemplateBuilder.PositionIn("front", ["front_dtf", "front"]).Should().Be("front");
        PrintifyTemplateBuilder.PositionIn("back", ["front", "neck"]).Should().BeNull();
        PrintifyTemplateBuilder.PositionIn("front", ["front_left_chest"]).Should().BeNull();
        PrintifyTemplateBuilder.PositionIn("front", ["front_side_dtf", "neck_outer"]).Should().Be("front_side_dtf");
        PrintifyTemplateBuilder.PositionIn("back", ["back_side"]).Should().Be("back_side");
        PrintifyTemplateBuilder.PositionIn("back", ["back_neck", "front"]).Should().BeNull();
        PrintifyTemplateBuilder.PositionIn("right", ["right_sleeve_embroidery"]).Should().Be("right_sleeve_embroidery");
    }

    [TestMethod]
    public void AspectWarning_OnlyWhenTheShapeIsOff()
    {
        PrintifyTemplateBuilder.AspectWarning("front", "front", new PrintAreaOnPhoto(0, 0, 690, 862), new PrintifyPlaceholder("front", 3600, 4500)).Should().BeNull();
        PrintifyTemplateBuilder.AspectWarning("front", "front", new PrintAreaOnPhoto(0, 0, 800, 800), new PrintifyPlaceholder("front", 3600, 4500)).Should().Contain("Check the provider");
    }
}
