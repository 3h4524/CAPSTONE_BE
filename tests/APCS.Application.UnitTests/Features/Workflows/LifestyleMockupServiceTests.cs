using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class LifestyleMockupServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6j8AAAAASUVORK5CYII=");
    private static readonly byte[] Garment = [1, 2, 3];

    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IImageGenerationProvider> provider = new();
    private readonly Mock<IApiKeyRepository> keys = new();
    private readonly Mock<IRepository<ApiUsageRecord>> usage = new();

    [TestMethod]
    public async Task GenerateAsync_OwnMockup_StoresPendingLifestyleImageInTheGarmentGroup()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product); garment.VariantKey = "#222222";
        IReadOnlyList<ImageReference>? sent = null;
        provider.Setup(x => x.ComposeAsync("plain-key", It.IsAny<string>(), It.IsAny<IReadOnlyList<ImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, IReadOnlyList<ImageReference> images, CancellationToken _) => sent = images)
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
        // The prompt calls the person the first image and the product the second.
        sent!.Select(x => x.Bytes).Should().BeEquivalentTo(new[] { Png, Garment }, x => x.WithStrictOrdering());
        usage.Verify(x => x.AddAsync(It.Is<ApiUsageRecord>(r => r.ProductId == product.Id && r.ModelName == "image-model" && r.CostUsd == 0.04m),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task GenerateAsync_NoConnectedGeminiKey_IsRejectedBeforeGeneration()
    {
        var product = fixture.AddProduct();
        var garment = fixture.AddAsset(product);

        var result = await Service(connected: false).GenerateAsync(product.Id, garment.Id, new MemoryStream(Png), Png.Length, default);

        result.Error.Code.Should().Be("MissingApiKey");
        provider.VerifyNoOtherCalls();
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

    private LifestyleMockupService Service(bool connected = true)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        var credentials = new Mock<IApiKeyCredentials>();
        credentials.Setup(x => x.Unprotect("encrypted")).Returns("plain-key");
        keys.Setup(x => x.ListOwnedAsync(WorkflowTestData.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ApiKey { ServiceProvider = "gemini", IsActive = true, LastCheckSucceeded = connected, KeyValueEncrypted = "encrypted" }]);
        fixture.Storage.Setup(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Garment);
        fixture.Storage.Setup(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string _, string key, CancellationToken _) => new StoredMockup(key, "7", new string('b', 64), 1024, 1536));
        fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>())).Returns("https://signed.example/preview");
        return new(current.Object, fixture.Products.Object, fixture.Assets.Object, usage.Object, keys.Object, credentials.Object,
            provider.Object, fixture.Storage.Object, fixture.MockupService, fixture.Unit.Object, fixture.Time,
            NullLogger<LifestyleMockupService>.Instance);
    }
}
