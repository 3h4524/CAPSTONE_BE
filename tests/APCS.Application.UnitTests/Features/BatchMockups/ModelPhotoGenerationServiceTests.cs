using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchMockups;

[TestClass]
public sealed class ModelPhotoGenerationServiceTests
{
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly Mock<IImageGenerationProvider> _provider = new();
    private readonly Mock<IModelPhotoService> _modelPhotos = new();
    private readonly Mock<IRepository<MockupTemplate>> _templates = new();
    private readonly List<MockupTemplate> _existing = [];
    private readonly Mock<IRepository<ApiUsageRecord>> _usage = new();
    private readonly Mock<IApiKeyRepository> _keys = new();
    private readonly Mock<IMockupMapService> _maps = new();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9];
    private readonly MockupTemplate _saved = new()
    {
        Id = Guid.NewGuid(), Name = "AI", ProductType = "tshirt", BaseImageUrl = "https://cdn/model.png", PrintAreaConfig = "{}",
        OutputWidthPx = 1728, OutputHeightPx = 2304, Kind = MockupRules.ModelKind
    };

    public ModelPhotoGenerationServiceTests()
    {
        _provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = ImageGenerationProviders.Ideogram, IsActive = true, LastCheckSucceeded = true, KeyValueEncrypted = "encrypted" }]);
        _modelPhotos.Setup(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MockupTemplateResponseDto(_saved.Id, "AI", "tshirt", _saved.BaseImageUrl, null, "{}", 1728, 2304, 0,
                false, true, true, true, null, null, Kind: MockupRules.ModelKind)));
        _templates.Setup(x => x.GetByIdAsync(_saved.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_saved);
        _templates.Setup(x => x.Query()).Returns(() => _existing.AsQueryable().BuildMock());
        _maps.Setup(x => x.ToJpegAsync(Png, It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);
    }

    [TestMethod]
    public async Task GenerateAsync_MakesThePhotoFromTheFixedPrompt_AndSavesItAsAnAiModelPhotoWhoseGarmentIsFound()
    {
        string? prompt = null, ratio = null;
        _provider.Setup(x => x.GenerateAsync("plain-key", It.IsAny<string>(), It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()))
            .Callback((string _, string text, string aspect, bool _, bool _, CancellationToken _) => { prompt = text; ratio = aspect; })
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        SaveModelPhotoRequestDto? saved = null;
        _modelPhotos.Setup(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), false, It.IsAny<CancellationToken>()))
            .Callback((SaveModelPhotoRequestDto request, bool _, CancellationToken _) => saved = request)
            .ReturnsAsync(Result.Success(new MockupTemplateResponseDto(_saved.Id, "AI", "tshirt", _saved.BaseImageUrl, null, "{}", 1728, 2304, 0,
                false, true, true, true, null, null, Kind: MockupRules.ModelKind)));

        var attributes = new ModelAttributesDto("single", "senior", "male");
        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", attributes), system: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.AiGenerated.Should().BeTrue();
        ratio.Should().Be("3:4");
        prompt.Should().Contain("a man in his late sixties").And.Contain("plain white crew-neck short-sleeve cotton t-shirt")
            .And.Contain("medium grey seamless backdrop");
        saved!.FindGarment.Should().BeTrue();
        // Made on a plain grey backdrop, so the garment is found the quicker way.
        saved.PlainBackdrop.Should().BeTrue();
        saved.ProductType.Should().Be("tshirt");
        // Kept as a JPEG: a 2K PNG photo can be over the upload limit.
        (saved.Photo!.ContentType, saved.Photo.Length).Should().Be(("image/jpeg", Jpeg.Length));
        saved.AutoPlacement.Should().BeTrue();
        saved.Attributes.Should().Be(attributes);
        saved.Name.Should().Be("Senior · Male", "who is in the photo, with no code or AI mark");
        _saved.AiGenerated.Should().BeTrue();
        MockupRules.IsAutomaticPlacement(_saved.PrintAreaConfig).Should().BeTrue();
        MockupRules.ParsePrintArea(_saved.PrintAreaConfig).Should().Be(ModelPhotoGenerationService.ChestArea(1728, 2304));
        _usage.Verify(x => x.AddAsync(It.Is<ApiUsageRecord>(r => r.UserId == Seller && r.CostUsd == 0.03m && r.Provider == "ideogram"),
            true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task FreeName_IsNumberedFromTwoWhenTaken()
    {
        _existing.AddRange([new MockupTemplate { Name = "Teen · Female" }, new MockupTemplate { Name = "teen · female 2" }, new MockupTemplate { Name = "Family" }]);

        (await Service().FreeNameAsync("Teen · Female", default)).Should().Be("Teen · Female 3");
        (await Service().FreeNameAsync("Couple", default)).Should().Be("Couple");
    }

    [TestMethod]
    public async Task GenerateAsync_WithoutAnIdeogramKey_IsRefusedBeforeAnythingIsMade()
    {
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: false);

        result.Error.Code.Should().Be("MissingApiKey");
        _provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_AttributesThatDoNotGoTogether_AreRefusedBeforeAnythingIsMade()
    {
        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("group", "adult", "female")), system: false);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("mug", null, "single")]
    [DataRow("tote_bag", null, "single")]
    [DataRow("tshirt", "seated", "group")]
    [DataRow("tshirt", "dancing", "single")]
    public async Task GenerateAsync_AProductNotWornOrAStyleThatDoesNotFit_IsRefusedBeforeAnythingIsMade(string product, string? pose, string people)
    {
        var attributes = people == "group" ? new ModelAttributesDto("group", GroupType: "friends") : new ModelAttributesDto("single", "adult", "female");

        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto(product, attributes, new(Pose: pose)), system: false);

        result.Error.Type.Should().Be(ErrorType.Validation);
        _provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_AHoodieInACafe_IsMadeInThatStyle_AndFoundOnABusyBackground()
    {
        string? prompt = null;
        _provider.Setup(x => x.GenerateAsync("plain-key", It.IsAny<string>(), It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()))
            .Callback((string _, string text, string _, bool _, bool _, CancellationToken _) => prompt = text)
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        SaveModelPhotoRequestDto? saved = null;
        _modelPhotos.Setup(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), false, It.IsAny<CancellationToken>()))
            .Callback((SaveModelPhotoRequestDto request, bool _, CancellationToken _) => saved = request)
            .ReturnsAsync(Result.Success(new MockupTemplateResponseDto(_saved.Id, "AI", "hoodie", _saved.BaseImageUrl, null, "{}", 1728, 2304, 0,
                false, true, true, true, null, null, Kind: MockupRules.ModelKind)));

        var result = await Service().GenerateAsync(
            new GenerateModelPhotoRequestDto("hoodie", new("single", "adult", "male"), new("cafe", "streetwear", "walking")), system: false);

        result.IsSuccess.Should().BeTrue();
        prompt.Should().Contain("plain white pullover hoodie").And.Contain("café").And.Contain("cargo trousers").And.Contain("walk toward the camera");
        saved!.ProductType.Should().Be("hoodie");
        saved.PlainBackdrop.Should().BeFalse("a café is not a plain backdrop");
        MockupRules.ParsePrintArea(_saved.PrintAreaConfig).Should().Be(ModelPhotoGenerationService.ChestArea(1728, 2304));
    }

    [TestMethod]
    public async Task GenerateAsync_AGroupInAStudio_KeepsTheFullMatteSoTheirGarmentsStayApart()
    {
        _provider.Setup(x => x.GenerateAsync("plain-key", It.IsAny<string>(), It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        SaveModelPhotoRequestDto? saved = null;
        _modelPhotos.Setup(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), false, It.IsAny<CancellationToken>()))
            .Callback((SaveModelPhotoRequestDto request, bool _, CancellationToken _) => saved = request)
            .ReturnsAsync(Result.Success(new MockupTemplateResponseDto(_saved.Id, "AI", "tshirt", _saved.BaseImageUrl, null, "{}", 2304, 1728, 0,
                false, true, true, true, null, null, Kind: MockupRules.ModelKind)));

        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("group", GroupType: "friends")), system: false);

        result.IsSuccess.Should().BeTrue();
        saved!.PlainBackdrop.Should().BeFalse("the quick matte can join two people's garments into one");
    }

    [TestMethod]
    public async Task GenerateAsync_BlockedByIdeogram_SaysSoAndSavesNothing()
    {
        _provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Blocked("safety", "ideogram-4"));

        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("group", GroupType: "family")), system: true);

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Message.Should().Contain("declined");
        _modelPhotos.Verify(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_GarmentNotFoundOnTheMadePhoto_PassesTheRefusalOn()
    {
        _provider.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));
        _modelPhotos.Setup(x => x.CreateAsync(It.IsAny<SaveModelPhotoRequestDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupTemplateResponseDto>(MockupErrors.GarmentNotFound()));

        var result = await Service().GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "teen", "male")), system: false);

        result.Error.Code.Should().Be("BatchMockups.GarmentNotFound");
        _usage.Verify(x => x.AddAsync(It.IsAny<ApiUsageRecord>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TestEntitlements PlatformKeysPlan() =>
        TestEntitlements.Plan(features: new Dictionary<string, PlanFeatureValue> { [PlanFeatures.SystemImageKeys] = new(true, null) });

    private void PlatformKeyMakes(string key) =>
        _provider.Setup(x => x.GenerateAsync(key, It.IsAny<string>(), It.IsAny<string>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-4", 0.03m));

    [TestMethod]
    public async Task GenerateAsync_PlanWithThePlatformsKeys_MakesThePhotoOnThemWithoutAKeyOfTheSellers()
    {
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlatformKeyMakes("platform-key");

        var result = await Service(PlatformKeysPlan(), new FakeSystemApiKeyPool("ideogram", ("platform-key", 5)))
            .GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: false);

        result.IsSuccess.Should().BeTrue();
        _usage.Verify(x => x.AddAsync(It.Is<ApiUsageRecord>(r => r.Feature == "image_generation"), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_ForTheSharedLibrary_IsMadeOnThePlatformsKeysWhateverThePlan()
    {
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        PlatformKeyMakes("platform-key");

        var result = await Service(TestEntitlements.Free(), new FakeSystemApiKeyPool("ideogram", ("platform-key", 5)))
            .GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: true);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task GenerateAsync_PlanWithoutThePlatformsKeys_StillNeedsTheSellersKey()
    {
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Service(TestEntitlements.Free(), new FakeSystemApiKeyPool("ideogram", ("platform-key", 5)))
            .GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: false);

        result.Error.Code.Should().Be("MissingApiKey");
    }

    [TestMethod]
    public async Task GenerateAsync_PlatformKeysDailyCapReached_IsRefusedBeforeAnythingIsMade()
    {
        var plan = PlatformKeysPlan();
        plan.CapsReached[PlanFeatures.DailyDesignImageCap] = true;

        var result = await Service(plan, new FakeSystemApiKeyPool("ideogram", ("platform-key", 5)))
            .GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: false);

        result.Error.Code.Should().Be("Plan.DailyCapReached");
        _provider.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_PlatformKeysUsedUp_WithoutAKeyOfTheSellers_SaysSo()
    {
        _keys.Setup(x => x.ListOwnedAsync(Seller, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Service(PlatformKeysPlan(), new FakeSystemApiKeyPool("ideogram", ("platform-key", 0)))
            .GenerateAsync(new GenerateModelPhotoRequestDto("tshirt", new("single", "adult", "female")), system: false);

        result.Error.Code.Should().Be("SystemImageKeysUsedUp");
        _usage.Verify(x => x.AddAsync(It.IsAny<ApiUsageRecord>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ModelPhotoGenerationService Service(IPlanEntitlementService? entitlements = null, ISystemApiKeyPool? systemKeys = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(Seller);
        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect("encrypted")).Returns("plain-key");
        return new ModelPhotoGenerationService(currentUser.Object, _modelPhotos.Object, _templates.Object, _usage.Object, _keys.Object,
            credentials.Object, [_provider.Object], _maps.Object, TimeProvider.System, NullLogger<ModelPhotoGenerationService>.Instance,
            entitlements, systemKeys);
    }
}
