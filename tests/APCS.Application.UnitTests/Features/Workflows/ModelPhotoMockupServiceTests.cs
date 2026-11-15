using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class ModelPhotoMockupServiceTests
{
    private static readonly MockupPosition TemplateArea = new(800, 600, 900, 1200);
    private static readonly MockupPosition ModelArea = new(400, 500, 300, 400);
    private static readonly StoredMockup Stored = new("stored-key", "3", new string('d', 64), 1000, 1500);

    private readonly WorkflowTestData _fixture = new();
    private readonly List<MockupTemplate> _templates = [];
    private readonly List<DesignImage> _designs = [];
    private readonly List<BatchJobProduct> _rows = [];
    private readonly List<BatchJob> _batches = [];
    private readonly Mock<IMockupCompositor> _compositor = new();
    private readonly Mock<IMockupMapService> _maps = new();
    private readonly ModelPhotoMockupService _service;

    public ModelPhotoMockupServiceTests()
    {
        _compositor.Setup(x => x.BuildAssetUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string baseUrl, string key) => $"{baseUrl}|{key}");
        // A composite URL that spells out what it was made from, so tests can tell where the design went.
        _compositor.Setup(x => x.BuildCompositeUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Returns((string baseUrl, string key, MockupPosition p, MockupLayers? layers) =>
                $"https://res.cloudinary.com/demo/image/upload/x_{p.X},y_{p.Y},w_{p.Width},h_{p.Height},co_{layers?.GarmentColor}/{key}/{baseUrl.GetHashCode():x}.jpg");
        _fixture.Storage.Setup(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, CancellationToken _) => Stored with { StorageKey = key });
        _fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), "image", It.IsAny<string?>(), false)).Returns("https://signed.example/preview");

        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        _service = new(current.Object, _fixture.Products.Object, _fixture.Assets.Object, Repository(_templates, x => x.Id).Object,
            Repository(_designs, x => x.Id).Object, Repository(_rows, x => x.Id).Object, Repository(_batches, x => x.Id).Object,
            _compositor.Object, _maps.Object, _fixture.Storage.Object, _fixture.MockupService, _fixture.Unit.Object, _fixture.Time,
            NullLogger<ModelPhotoMockupService>.Instance);
    }

    [TestMethod]
    public async Task GenerateAsync_RecolorsTheModelsGarmentAndPrintsTheDesignWhereTheMockupHasIt()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, ModelArea,
            It.Is<MockupLayers?>(layers => layers!.GarmentColor == "#1F2A44" && layers.GarmentMaskKey != null && layers.BackgroundColor == null)), Times.Once);
        var made = _fixture.AssetRows.Single(x => x.Id == result.Value.Id);
        made.Role.Should().Be("Lifestyle");
        made.ApprovalStatus.Should().Be("pending");
        made.ContentHash.Should().Be(Stored.Hash);
        made.MockupTemplateId.Should().Be(model.Id);
        made.DesignImageId.Should().Be(design.Id);
        made.ArtworkGroupKey.Should().Be(garment.ArtworkGroupKey);
        made.VariantKey.Should().Be("#1F2A44");
        model.UsageCount.Should().Be(1);
        _fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_PrintAreaMovedForThisMockup_MovesItOnTheModelToo()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var moved = new MockupPosition(1100, 600, 900, 600);
        var row = new BatchJobProduct { Id = Guid.NewGuid(), BatchJobId = Guid.NewGuid() };
        _rows.Add(row);
        _batches.Add(new BatchJob { Id = row.BatchJobId, Config = Overrides(MockupRules.SlotKey(design.Id, template.Id, "#1F2A44"), template.Id, "#1F2A44", moved) });
        garment.BatchJobProductId = row.Id;
        garment.MockupImageUrl = _compositor.Object.BuildCompositeUrl(template.BaseImageUrl, design.StorageKey, moved, MockupRules.Layers(template, design, "#1F2A44"));
        var model = AddModelPhoto();

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, new MockupPosition(500, 500, 300, 200), It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_MovedPrintAreaThisMockupWasNotMadeWith_UsesTheTemplatesOwn()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var row = new BatchJobProduct { Id = Guid.NewGuid(), BatchJobId = Guid.NewGuid() };
        _rows.Add(row);
        _batches.Add(new BatchJob { Id = row.BatchJobId, Config = Overrides(MockupRules.SlotKey(design.Id, template.Id, "#1F2A44"), template.Id, "#1F2A44", new MockupPosition(1100, 600, 900, 600)) });
        garment.BatchJobProductId = row.Id;
        var model = AddModelPhoto();

        await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, ModelArea, It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_MockupWithSeveralPrintAreas_CarriesItsMainAreaOntoTheModel()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var row = new BatchJobProduct { Id = Guid.NewGuid(), BatchJobId = Guid.NewGuid() };
        _rows.Add(row);
        var slot = MockupRules.SlotKey(design.Id, template.Id, "#1F2A44");
        _batches.Add(new BatchJob
        {
            Id = row.BatchJobId,
            Config = $"{{\"mockupOverrides\":{{\"{slot}\":{{\"mockupTemplateId\":\"{template.Id}\",\"garmentColor\":\"#1F2A44\",\"areas\":["
                + "{\"key\":\"main\",\"x\":1100,\"y\":600,\"width\":900,\"height\":600,\"rotation\":10,\"source\":\"same\"}]}}}"
        });
        garment.BatchJobProductId = row.Id;
        var model = AddModelPhoto();

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, new MockupPosition(500, 500, 300, 200), It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_MockupOfTheBackOfTheGarment_IsRefused()
    {
        var (product, _, template, garment) = Mockup("#1F2A44");
        template.PrintAreaConfig = MockupRules.SerializePrintLayout(MockupRules.ViewBack,
            MockupRules.ParsePrintLayout(template.PrintAreaConfig)!.Areas);
        var model = AddModelPhoto();

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("front");
    }

    [TestMethod]
    public async Task GenerateAsync_PhotoOfSeveralPeople_PutsTheDesignOnEachOfTheirShirts()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();
        model.PrintAreaConfig = MockupRules.SerializePrintArea(ModelArea, automatic: true);
        ModelPeople.Apply(model, new ModelAttributesDto("group", GroupType: "couple"));
        _maps.Setup(x => x.MeasureGarmentAsync(It.Is<string>(url => url.StartsWith(template.BaseImageUrl)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GarmentMeasurement(0.1, 0.9, 0.5, 0.5));
        _maps.Setup(x => x.MeasureGarmentsAsync(It.Is<string>(url => url.StartsWith(model.BaseImageUrl)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GarmentMeasurement(0.2, 0.7, 0.3, 0.2), new GarmentMeasurement(0.25, 0.7, 0.7, 0.2)]);
        MockupLayers? layers = null;
        MockupPosition? first = null;
        _compositor.Setup(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, It.IsAny<MockupPosition>(), It.IsAny<MockupLayers?>()))
            .Callback((string _, string _, MockupPosition position, MockupLayers? given) => { first = position; layers = given; })
            .Returns("https://res.cloudinary.com/demo/image/upload/couple.jpg");

        var result = await _service.PreviewAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        var more = layers!.MorePlacements.Should().ContainSingle().Subject;
        // Each shirt gets the design at its own size, placed on its own body as on the mock-up (a little right of
        // the middle there): the two sit as far apart as the shirts, 400 px.
        (first!.X + first.Width / 2.0).Should().BeApproximately(311, 2);
        (more.X + more.Width / 2.0 - (first.X + first.Width / 2.0)).Should().BeApproximately(400, 2);
        more.Width.Should().Be(first.Width);
        _maps.Verify(x => x.MeasureGarmentAsync(It.Is<string>(url => url.StartsWith(model.BaseImageUrl)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_AutomaticPlacement_PlacesTheDesignByMeasuringBothGarments()
    {
        var (product, design, template, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();
        model.PrintAreaConfig = MockupRules.SerializePrintArea(ModelArea, automatic: true);
        // The tee's body is half the template photo wide and starts a tenth down; the model's is 0.4 wide from 0.2 down.
        _maps.Setup(x => x.MeasureGarmentAsync(It.Is<string>(url => url.StartsWith(template.BaseImageUrl)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GarmentMeasurement(0.1, 0.9, 0.5, 0.5));
        _maps.Setup(x => x.MeasureGarmentAsync(It.Is<string>(url => url.StartsWith(model.BaseImageUrl)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GarmentMeasurement(0.2, 0.7, 0.5, 0.4));

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        // A third of the size (400 / 1200 px of body), over the three quarters of its flat width a worn garment shows:
        // 4/9, as far left of the body's center and below its top in those terms.
        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, new MockupPosition(322, 460, 400, 533), It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_AutomaticPlacementButAGarmentCannotBeMeasured_UsesTheModelPhotosPrintArea()
    {
        var (product, design, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();
        model.PrintAreaConfig = MockupRules.SerializePrintArea(ModelArea, automatic: true);
        _maps.Setup(x => x.MeasureGarmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GarmentMeasurement?)null);

        await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, ModelArea, It.IsAny<MockupLayers?>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_PrintAreaMarkedByItsCorners_WarpsTheDesignOntoThem()
    {
        var (product, design, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();
        MockupPoint[] quad = [new(400, 500), new(700, 520), new(690, 900), new(410, 880)];
        model.PrintAreaConfig = MockupRules.SerializePrintQuad(quad);

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        var expected = MockupRules.MapPlacementOnQuad(TemplateArea, TemplateArea, design.ImageWidthPx, design.ImageHeightPx, quad);
        _compositor.Verify(x => x.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, MockupRules.QuadBounds(expected),
            It.Is<MockupLayers?>(layers => layers!.DesignCorners != null && layers.DesignCorners.SequenceEqual(expected))), Times.Once);
    }

    [TestMethod]
    public async Task PreviewAsync_GivesTheImageThatWouldBeMadeWithoutStoringAnything()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();
        var before = _fixture.AssetRows.Count;

        var preview = await _service.PreviewAsync(product.Id, new(garment.Id, model.Id), default);
        var made = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        preview.IsSuccess.Should().BeTrue();
        preview.Value.Url.Should().Be(_fixture.AssetRows.Single(x => x.Id == made.Value.Id).MockupImageUrl);
        _fixture.AssetRows.Should().HaveCount(before + 1, "only Generate stores an image");
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PreviewAsync_ChecksAsGenerateDoes()
    {
        var (product, _, _, garment) = Mockup(null);
        var model = AddModelPhoto();

        var preview = await _service.PreviewAsync(product.Id, new(garment.Id, model.Id), default);

        preview.Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task GenerateAsync_PrintAreaPlacement_DoesNotMeasureTheGarments()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();

        await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        _maps.Verify(x => x.MeasureGarmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_MockupInItsPhotosOwnColor_ReadsTheColorFromThePhoto()
    {
        var (product, _, template, garment) = Mockup(null);
        var model = AddModelPhoto();
        _maps.Setup(x => x.MeasureGarmentColorAsync(template.BaseImageUrl, It.Is<string>(url => url.StartsWith(template.BaseImageUrl)), It.IsAny<CancellationToken>()))
            .ReturnsAsync("#2b2b2b");

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.IsSuccess.Should().BeTrue();
        _fixture.AssetRows.Single(x => x.Id == result.Value.Id).GarmentColor.Should().Be("#2B2B2B");
    }

    [TestMethod]
    public async Task GenerateAsync_ColorThatCannotBeRead_IsRefusedUnlessOneIsGiven()
    {
        var (product, _, _, garment) = Mockup(null);
        var model = AddModelPhoto();

        var refused = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);
        var chosen = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id, "#c0392b"), default);

        refused.Error.Type.Should().Be(ErrorType.Validation);
        chosen.IsSuccess.Should().BeTrue();
        _fixture.AssetRows.Single(x => x.Id == chosen.Value.Id).GarmentColor.Should().Be("#C0392B");
    }

    [TestMethod]
    public async Task GenerateAsync_MockupWithItsColor_DoesNotReadThePhoto()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();

        await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        _maps.Verify(x => x.MeasureGarmentColorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_SameAgain_ReturnsTheImageAlreadyMade()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        var model = AddModelPhoto();

        var first = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);
        var second = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        second.Value.Id.Should().Be(first.Value.Id);
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_UploadedMockupOrLifestyleImage_HasNoDesignToCopy()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        var uploaded = _fixture.AddAsset(product);
        var model = AddModelPhoto();
        var lifestyle = (await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default)).Value;

        (await _service.GenerateAsync(product.Id, new(uploaded.Id, model.Id), default)).Error.Type.Should().Be(ErrorType.Validation);
        (await _service.GenerateAsync(product.Id, new(lifestyle.Id, model.Id), default)).Error.Type.Should().Be(ErrorType.Validation);
    }

    [TestMethod]
    public async Task GenerateAsync_ModelPhotoOfAnotherSellerOrProductTypeOrAGarmentTemplate_IsRefused()
    {
        var (product, _, template, garment) = Mockup("#1F2A44");
        var foreign = AddModelPhoto(); foreign.UserId = Guid.NewGuid();
        var mug = AddModelPhoto(); mug.ProductType = "mug";

        (await _service.GenerateAsync(product.Id, new(garment.Id, foreign.Id), default)).Error.Type.Should().Be(ErrorType.NotFound);
        (await _service.GenerateAsync(product.Id, new(garment.Id, mug.Id), default)).Error.Code.Should().Be("BatchMockups.TemplateNotForProduct");
        (await _service.GenerateAsync(product.Id, new(garment.Id, template.Id), default)).Error.Type.Should().Be(ErrorType.NotFound);
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_AnotherSellersProduct_IsNotFound()
    {
        var (product, _, _, garment) = Mockup("#1F2A44");
        product.UserId = Guid.NewGuid();
        var model = AddModelPhoto();

        var result = await _service.GenerateAsync(product.Id, new(garment.Id, model.Id), default);

        result.Error.Code.Should().Be("MockupNotFound");
    }

    // A tee mock-up as a batch composites it, already imported as a video source.
    private (Product Product, DesignImage Design, MockupTemplate Template, MockupImage Garment) Mockup(string? color)
    {
        var product = _fixture.AddProduct();
        var design = new DesignImage { Id = Guid.NewGuid(), ProductId = product.Id, StorageKey = $"designs/{Guid.NewGuid():N}", ImageWidthPx = 1024, ImageHeightPx = 1024 };
        _designs.Add(design);
        var templateUrl = $"https://res.cloudinary.com/demo/image/upload/v1/tee-{Guid.NewGuid():N}.jpg";
        var template = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = "Flat tee", ProductType = "tshirt", BaseImageUrl = templateUrl, PrintAreaConfig = MockupRules.SerializePrintArea(TemplateArea),
            OutputWidthPx = 2400, OutputHeightPx = 2400, IsActive = true, Kind = MockupRules.GarmentKind, AllowRecolor = true, GarmentIsLight = true,
            PrintMapsSourceUrl = templateUrl, PrintMapsVersion = 5
        };
        _templates.Add(template);
        var garment = new MockupImage
        {
            Id = Guid.NewGuid(), ProductId = product.Id, DesignImageId = design.Id, MockupTemplateId = template.Id, SourceType = MockupSources.Generated,
            StorageProvider = "cloudinary", StorageKey = "stored", StorageVersion = "1", ContentHash = new string('a', 64),
            MockupImageUrl = _compositor.Object.BuildCompositeUrl(templateUrl, design.StorageKey, TemplateArea, MockupRules.Layers(template, design, color)),
            MockupWidthPx = 2400, MockupHeightPx = 2400, ApprovalStatus = "pending", MetadataRevision = 1, Role = "Hero", Regions = "{}",
            GarmentColor = color, ArtworkGroupKey = design.Id.ToString(), VariantKey = color, CreatedAt = WorkflowTestData.Now.UtcDateTime
        };
        _fixture.AssetRows.Add(garment);
        return (product, design, template, garment);
    }

    private MockupTemplate AddModelPhoto()
    {
        var url = $"https://res.cloudinary.com/demo/image/upload/v1/model-{Guid.NewGuid():N}.jpg";
        var model = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = "Studio model", ProductType = "tshirt", BaseImageUrl = url, PrintAreaConfig = MockupRules.SerializePrintArea(ModelArea),
            OutputWidthPx = 1000, OutputHeightPx = 1500, IsActive = true, IsSystemTemplate = true, Kind = MockupRules.ModelKind, AllowRecolor = true,
            GarmentIsLight = true, PrintMapsSourceUrl = url, PrintMapsVersion = 9
        };
        _templates.Add(model);
        return model;
    }

    private static string Overrides(string slotKey, Guid templateId, string? color, MockupPosition area) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [MockupRules.OverridesConfigKey] = new Dictionary<string, MockupOverrideDto>
            {
                [slotKey] = new(templateId, color, null, new MockupPrintAreaDto(area.X, area.Y, area.Width, area.Height))
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static Mock<IRepository<T>> Repository<T>(List<T> rows, Func<T, Guid> id) where T : class
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        mock.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object key, CancellationToken _) => rows.SingleOrDefault(x => id(x) == (Guid)key));
        mock.Setup(x => x.UpdateAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }
}
