using APCS.Application.Abstractions.AI;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class LifestyleMockupServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6j8AAAAASUVORK5CYII=");
    private static readonly byte[] Garment = [1, 2, 3];
    private static readonly byte[] Artwork = [4, 5, 6];

    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IImageGenerationProvider> provider = new();
    private readonly Mock<IImageGenerationProvider> gemini = new(MockBehavior.Strict);
    private readonly Mock<IApiKeyRepository> keys = new();
    private readonly Mock<IRepository<ApiUsageRecord>> usage = new();
    private readonly Mock<IRepository<DesignImage>> designs = new();
    private readonly Mock<IRepository<MockupTemplate>> templates = new();
    private readonly Mock<IMockupMapService> maps = new();
    private readonly List<MockupTemplate> modelPhotos = [];

    [TestMethod]
    public async Task GenerateAsync_OwnMockup_StoresPendingLifestyleImageInTheGarmentGroup()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product); garment.VariantKey = "#222222";
        IReadOnlyList<ImageReference>? sent = null;
        string? prompt = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string text, IReadOnlyList<ImageReference> images, CancellationToken _) => { prompt = text; sent = images; })
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "image-model", 0.04m));

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.IsSuccess.Should().BeTrue();
        var created = fixture.AssetRows.Single(x => x.Id == result.Value.Id);
        created.Role.Should().Be("Lifestyle");
        created.SourceType.Should().Be("uploaded");
        created.ApprovalStatus.Should().Be("pending");
        created.ApprovedRevision.Should().BeNull();
        created.ArtworkGroupKey.Should().Be(garment.ArtworkGroupKey);
        created.VariantKey.Should().Be("#222222");
        // An uploaded mock-up has no design, so the person photo is edited with the mock-up alone.
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment }, x => x.WithStrictOrdering());
        prompt.Should().Be(LifestyleMockupService.Prompt(withDesign: false, designText: null));
        usage.Verify(x => x.AddAsync(It.Is<ApiUsageRecord>(r => r.ProductId == product.Id && r.Provider == "ideogram" && r.ModelName == "image-model" && r.CostUsd == 0.04m),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsync_MockupMadeFromADesign_SendsTheDesignToCopyThePrintFrom()
    {
        var product = fixture.AddProduct(); product.DesiredDesignText = "Snacking \"around\" the World";
        var garment = fixture.AddAsset(product);
        var design = new DesignImage { Id = Guid.NewGuid(), ProductId = product.Id, StorageKey = "design-images/abc" };
        garment.DesignImageId = design.Id;
        designs.Setup(x => x.GetByIdAsync(design.Id, It.IsAny<CancellationToken>())).ReturnsAsync(design);
        fixture.Storage.Setup(x => x.ReadPublicImageAsync("design-images/abc", It.IsAny<CancellationToken>())).ReturnsAsync(Artwork);
        IReadOnlyList<ImageReference>? sent = null;
        string? prompt = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string text, IReadOnlyList<ImageReference> images, CancellationToken _) => { prompt = text; sent = images; })
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "image-model", 0.06m));

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.IsSuccess.Should().BeTrue();
        // The person photo is edited; the mock-up gives the cut and colour, the design the print.
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment, Artwork }, x => x.WithStrictOrdering());
        prompt.Should().Contain("artwork alone").And.Contain("reads exactly \"Snacking 'around' the World\"").And.Contain("do not copy any neck label");
    }

    [TestMethod]
    public async Task GenerateAsync_BatchMockupNotYetStored_IsReadFromItsComposite()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);
        garment.SourceType = "generated"; garment.ContentHash = null; garment.MockupImageUrl = "https://res.cloudinary.com/demo/image/upload/hero.jpg";
        fixture.Storage.Setup(x => x.ReadCompositeAsync(garment.MockupImageUrl, It.IsAny<CancellationToken>())).ReturnsAsync(Garment);
        IReadOnlyList<ImageReference>? sent = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyList<ImageReference> images, CancellationToken _) => sent = images)
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "image-model", 0.06m));

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.IsSuccess.Should().BeTrue();
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment }, x => x.WithStrictOrdering());
        fixture.Storage.Verify(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_UploadedMockupWithoutAFile_IsNotFound()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product); garment.ContentHash = null; garment.MockupImageUrl = "https://res.cloudinary.com/demo/image/upload/x.jpg";

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.Error.Code.Should().Be("MockupNotFound");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GenerateAsync_ForeignProduct_DoesNotCallTheProvider()
    {
        var product = fixture.AddProduct(); product.UserId = Guid.NewGuid();
        var garment = fixture.AddAsset(product);

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.Error.Code.Should().Be("MockupNotFound");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GenerateAsync_MockupOfAnotherProduct_IsNotFound()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(fixture.AddProduct());

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.Error.Code.Should().Be("MockupNotFound");
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GenerateAsync_NoConnectedIdeogramKey_IsRejectedBeforeGeneration()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);

        var result = await Service(connected: false).GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.Error.Code.Should().Be("MissingApiKey");
        result.Error.Message.Should().Contain("Ideogram");
        provider.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateAsync_NotAnImage_IsRejectedBeforeGeneration()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);
        var text = "not an image, just text"u8.ToArray();

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(text), text.Length, default);

        result.IsFailure.Should().BeTrue();
        provider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GenerateAsync_ProviderBlocksThePhoto_StoresNothing()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);
        provider.Setup(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageGenerationResult.Blocked("IMAGE_SAFETY", "image-model"));

        var result = await Service().GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.IsFailure.Should().BeTrue();
        fixture.AssetRows.Should().ContainSingle();
        fixture.Storage.Verify(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateFromModelPhotoAsync_GroupPhoto_DressesEveryoneOnTheLibraryPhoto()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);
        var family = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = "Family", ProductType = "tshirt", BaseImageUrl = "https://cdn/family.jpg", PrintAreaConfig = "{}",
            IsActive = true, Kind = MockupRules.ModelKind, UserId = null
        };
        ModelPeople.Apply(family, new ModelAttributesDto("group", GroupType: "family"));
        modelPhotos.Add(family);
        maps.Setup(x => x.DownloadAsync("https://cdn/family.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Png);
        IReadOnlyList<ImageReference>? sent = null;
        string? prompt = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string text, IReadOnlyList<ImageReference> images, CancellationToken _) => { prompt = text; sent = images; })
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "image-model", 0.03m));

        var result = await Service().GenerateFromModelPhotoAsync(product.Id, garment.Id, family.Id, default);

        result.IsSuccess.Should().BeTrue();
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment }, x => x.WithStrictOrdering());
        prompt.Should().Be(LifestyleMockupService.Prompt(withDesign: false, designText: null, group: true));
        prompt.Should().Contain("Dress every person");
    }

    [TestMethod]
    public async Task GenerateFromModelPhotoAsync_AnotherSellersModelPhoto_IsNotFound()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);
        var foreign = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = "Theirs", ProductType = "tshirt", BaseImageUrl = "https://cdn/theirs.jpg", PrintAreaConfig = "{}",
            IsActive = true, Kind = MockupRules.ModelKind, UserId = Guid.NewGuid()
        };
        modelPhotos.Add(foreign);

        var result = await Service().GenerateFromModelPhotoAsync(product.Id, garment.Id, foreign.Id, default);

        result.Error.Type.Should().Be(APCS.Common.Models.ErrorType.NotFound);
        provider.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GenerateForVideoAsync_SendsTheLibraryModelAndTheMockupToIdeogramWithTheSellersIdeogramKey()
    {
        var product = fixture.AddProduct();
        var hero = fixture.AddAsset(product);
        var model = new MockupTemplate
        {
            Id = Guid.NewGuid(), Name = "Ava", ProductType = "tshirt", BaseImageUrl = "https://cdn/ava.jpg", PrintAreaConfig = "{}",
            IsActive = true, Kind = MockupRules.ModelKind, UserId = null
        };
        modelPhotos.Add(model);
        maps.Setup(x => x.DownloadAsync("https://cdn/ava.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Png);
        IReadOnlyList<ImageReference>? sent = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyList<ImageReference> images, CancellationToken _) => sent = images)
            .ReturnsAsync(ImageGenerationResult.Success(Png, "image/png", "ideogram-image", 0.04m));
        var service = Service();

        var result = await service.GenerateForVideoAsync(product.Id, hero.Id, model.Id, default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment }, x => x.WithStrictOrdering());
        fixture.AssetRows.Should().ContainSingle(x => x.Role == "Lifestyle").Which.ApprovalStatus.Should().Be("pending");
        usage.Verify(x => x.AddAsync(It.Is<ApiUsageRecord>(r => r.Provider == "ideogram" && r.CostUsd == 0.04m), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        gemini.Verify(x => x.ComposeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private LifestyleMockupService Service(bool connected = true)
    {
        templates.Setup(x => x.Query()).Returns(() => modelPhotos.AsQueryable().BuildMock());
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect("encrypted")).Returns("plain-key");
        credentials.Setup(x => x.Unprotect("gemini-encrypted")).Returns("gemini-key");
        keys.Setup(x => x.ListOwnedAsync(WorkflowTestData.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = "gemini", IsActive = true, LastCheckSucceeded = true, KeyValueEncrypted = "gemini-encrypted" },
             new ApiKey { ServiceProvider = "ideogram", IsActive = true, LastCheckSucceeded = connected, KeyValueEncrypted = "encrypted" }]);
        fixture.Storage.Setup(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Garment);
        fixture.Storage.Setup(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, string key, CancellationToken _) => new StoredMockup(key, "7", new string('b', 64), 1024, 1536));
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns("https://signed.example/preview");
        // Gemini comes first, as nothing about the order picks the provider: Ideogram edits the photo.
        provider.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Ideogram);
        gemini.SetupGet(x => x.Provider).Returns(ImageGenerationProviders.Gemini);
        var service = new LifestyleMockupService(current.Object, fixture.Products.Object, fixture.Assets.Object, designs.Object, usage.Object, keys.Object,
            credentials.Object, [gemini.Object, provider.Object], fixture.Storage.Object, fixture.MockupService, fixture.Unit.Object,
            fixture.Time, NullLogger<LifestyleMockupService>.Instance, templates.Object, maps.Object,
            TestEntitlements.AllowAll());
        // Picking Ideogram reads its name; checked here, so a test can still verify nothing else was asked of it.
        provider.VerifyGet(x => x.Provider);
        return service;
    }
}
