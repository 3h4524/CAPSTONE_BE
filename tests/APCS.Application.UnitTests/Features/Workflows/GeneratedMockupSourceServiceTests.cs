using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class GeneratedMockupSourceServiceTests
{
    private const string CompositeUrl = "https://res.cloudinary.com/demo/image/upload/l_design/fl_layer_apply/base.jpg";
    private static readonly StoredMockup Stored = new("stored-key", "17", new string('b', 64), 1200, 1500);

    private readonly WorkflowTestData _fixture = new();
    private readonly GeneratedMockupSourceService _service;

    public GeneratedMockupSourceServiceTests()
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
        _fixture.Storage.Setup(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, CancellationToken _) => Stored with { StorageKey = key });
        _fixture.Storage.Setup(x => x.SignRead(It.IsAny<string>(), "image", It.IsAny<string?>(), false)).Returns("https://signed.example/preview");
        _service = new(current.Object, _fixture.Products.Object, _fixture.Assets.Object, _fixture.Unit.Object,
            _fixture.Storage.Object, _fixture.MockupService, NullLogger<GeneratedMockupSourceService>.Instance);
    }

    [TestMethod]
    public async Task ImportAsync_CompositedMockup_StoresASnapshotAndLeavesItForApproval()
    {
        var product = _fixture.AddProduct();
        var designImageId = Guid.NewGuid();
        var mockup = AddComposited(product, designImageId, garmentColor: "#1F2A44");

        var result = await _service.ImportAsync(product.Id, default);

        result.Value.ImportedCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(0);
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(CompositeUrl, $"mockups/{WorkflowTestData.Owner}/{product.Id}/{mockup.Id}", default), Times.Once);
        mockup.StorageKey.Should().Be($"mockups/{WorkflowTestData.Owner}/{product.Id}/{mockup.Id}");
        mockup.StorageVersion.Should().Be("17");
        mockup.ContentHash.Should().Be(Stored.Hash);
        (mockup.MockupWidthPx, mockup.MockupHeightPx).Should().Be((1200, 1500));
        mockup.ArtworkGroupKey.Should().Be(designImageId.ToString());
        mockup.VariantKey.Should().Be("#1F2A44");
        // Approving stays a decision made at the Mockup Approval gate.
        mockup.ApprovalStatus.Should().Be("pending");
        mockup.ApprovedRevision.Should().BeNull();
        result.Value.Mockups.Should().ContainSingle(x => x.Id == mockup.Id && x.SourceType == "generated");
        _fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ImportAsync_MockupWithItsOwnGroupAndVariant_KeepsThem()
    {
        var product = _fixture.AddProduct();
        var mockup = AddComposited(product, Guid.NewGuid(), garmentColor: "#FFFFFF");
        mockup.ArtworkGroupKey = "summer-drop";
        mockup.VariantKey = "white";

        await _service.ImportAsync(product.Id, default);

        mockup.ArtworkGroupKey.Should().Be("summer-drop");
        mockup.VariantKey.Should().Be("white");
    }

    [TestMethod]
    public async Task ImportAsync_AlreadyImportedUploadedAndDeletedMockups_AreLeftAlone()
    {
        var product = _fixture.AddProduct();
        var imported = AddComposited(product, Guid.NewGuid()); imported.ContentHash = new string('c', 64);
        var deleted = AddComposited(product, Guid.NewGuid()); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        _fixture.AddAsset(product);
        AddComposited(_fixture.AddProduct(), Guid.NewGuid());

        var result = await _service.ImportAsync(product.Id, default);

        result.Value.ImportedCount.Should().Be(0);
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        deleted.ContentHash.Should().BeNull();
    }

    [TestMethod]
    public async Task ImportAsync_WhenOneUploadFails_ImportsTheOthersAndCountsTheFailure()
    {
        var product = _fixture.AddProduct();
        var broken = AddComposited(product, Guid.NewGuid());
        var fine = AddComposited(product, Guid.NewGuid());
        _fixture.Storage.Setup(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.Is<string>(key => key.EndsWith(broken.Id.ToString())), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid image or image exceeds 32 megapixels."));

        var result = await _service.ImportAsync(product.Id, default);

        result.Value.ImportedCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(1);
        broken.ContentHash.Should().BeNull();
        fine.ContentHash.Should().Be(Stored.Hash);
        // The one that failed has no stored file, so it is not offered as a source.
        result.Value.Mockups.Select(x => x.Id).Should().Equal(fine.Id);
    }

    [TestMethod]
    [DataRow("http://res.cloudinary.com/demo/image/upload/base.jpg")]
    [DataRow("https://example.com/image/upload/base.jpg")]
    [DataRow("not a url")]
    public async Task ImportAsync_UrlThatIsNotACompositeOfOurs_IsNeverFetched(string url)
    {
        var product = _fixture.AddProduct();
        AddComposited(product, Guid.NewGuid()).MockupImageUrl = url;

        var result = await _service.ImportAsync(product.Id, default);

        result.Value.FailedCount.Should().Be(1);
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ImportAsync_MoreThanOneCallAllows_ImportsTheNewestFirst()
    {
        var product = _fixture.AddProduct();
        var mockups = Enumerable.Range(0, GeneratedMockupSourceService.MaximumPerCall + 2)
            .Select(i => { var mockup = AddComposited(product, Guid.NewGuid()); mockup.CreatedAt = WorkflowTestData.Now.AddMinutes(i).UtcDateTime; return mockup; })
            .ToArray();

        var result = await _service.ImportAsync(product.Id, default);

        result.Value.ImportedCount.Should().Be(GeneratedMockupSourceService.MaximumPerCall);
        mockups.Take(2).Should().OnlyContain(x => x.ContentHash == null);
        mockups.Skip(2).Should().OnlyContain(x => x.ContentHash != null);
    }

    [TestMethod]
    public async Task ImportAsync_ForeignOrDeletedProduct_ImportsNothing()
    {
        var foreign = _fixture.AddProduct(); foreign.UserId = Guid.NewGuid();
        AddComposited(foreign, Guid.NewGuid());
        var deleted = _fixture.AddProduct(); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        AddComposited(deleted, Guid.NewGuid());

        (await _service.ImportAsync(foreign.Id, default)).Error.Code.Should().Be("MockupNotFound");
        (await _service.ImportAsync(deleted.Id, default)).Error.Code.Should().Be("MockupNotFound");
        (await _service.ImportAsync(Guid.NewGuid(), default)).Error.Code.Should().Be("MockupNotFound");
        _fixture.Storage.Verify(x => x.UploadMockupFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListAsync_CompositedMockupNotImportedYet_IsNotOfferedAsASource()
    {
        var product = _fixture.AddProduct();
        AddComposited(product, Guid.NewGuid());
        var uploaded = _fixture.AddAsset(product);

        var result = await _fixture.MockupService.ListAsync(product.Id, default);

        result.Value.Select(x => x.Id).Should().Equal(uploaded.Id);
    }

    // A mock-up as the batch flow composites it: a transformation URL with a placeholder storage key.
    private MockupImage AddComposited(Product product, Guid designImageId, string? garmentColor = null)
    {
        var mockup = new MockupImage
        {
            Id = Guid.NewGuid(), ProductId = product.Id, DesignImageId = designImageId, MockupTemplateId = Guid.NewGuid(),
            SourceType = "generated", StorageProvider = "cloudinary", StorageKey = "mockups/template/design",
            MockupImageUrl = CompositeUrl, MockupWidthPx = 1000, MockupHeightPx = 1000, ApprovalStatus = "pending",
            MetadataRevision = 1, Role = "Hero", Regions = "{}", GarmentColor = garmentColor,
            CreatedAt = WorkflowTestData.Now.UtcDateTime
        };
        _fixture.AssetRows.Add(mockup);
        return mockup;
    }
}
