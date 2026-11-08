using System.Buffers.Binary;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class MockupAssetServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6j8AAAAASUVORK5CYII=");

    [TestMethod]
    public async Task ListAsync_ForeignProduct_DoesNotIssueSourcePreviews()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct(); product.UserId = Guid.NewGuid();
        fixture.AddAsset(product);
        var result = await fixture.MockupService.ListAsync(product.Id, default);
        result.Error.Code.Should().Be("MockupNotFound");
        fixture.Storage.Verify(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task ListAsync_OwnProduct_FiltersDeletedAndOtherProductAndOrdersByRecency()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var older = fixture.AddAsset(product); older.CreatedAt = WorkflowTestData.Now.UtcDateTime;
        var latest = fixture.AddAsset(product); latest.CreatedAt = WorkflowTestData.Now.AddMinutes(1).UtcDateTime;
        var deleted = fixture.AddAsset(product); deleted.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        fixture.AddAsset(fixture.AddProduct());
        var result = await fixture.MockupService.ListAsync(product.Id, default);
        result.Value.Select(x => x.Id).Should().Equal(latest.Id, older.Id);
        result.Value.Should().OnlyContain(x => x.ProductId == product.Id && x.ApprovedRevision == 3);
    }

    [TestMethod]
    public async Task ReviewAsync_StaleRevision_DoesNotApproveChangedCrop()
    {
        var fixture = new WorkflowTestData();
        var asset = fixture.AddAsset(fixture.AddProduct()); asset.ApprovalStatus = "pending"; asset.ApprovedRevision = null;
        var result = await fixture.MockupService.ReviewAsync(asset.Id, new(2, true), default);
        result.Error.Code.Should().Be("StaleRevision");
        asset.ApprovalStatus.Should().Be("pending");
        asset.ApprovedRevision.Should().BeNull();
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }


    [TestMethod]
    public async Task UploadAsync_StaticImage_NormalizesStorageAndRequiresNewApproval()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        byte[]? uploaded = null;
        fixture.Storage.Setup(x => x.UploadMockupAsync(It.IsAny<Stream>(), "mockup.png", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((Stream stream, string _, string _, CancellationToken _) => uploaded = ((MemoryStream)stream).ToArray())
            .ReturnsAsync(new StoredMockup("private/normalized", "42", new string('f', 64), 1200, 2400));
        fixture.Storage.Setup(x => x.SignRead("private/normalized", "image", "42", false)).Returns("https://example.invalid/expiring");
        using var stream = new MemoryStream(Png);
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.MockupService.UploadAsync(product.Id, stream, "/unsafe/mockup.png", Png.Length, cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        uploaded.Should().Equal(Png);
        var asset = fixture.AssetRows.Single();
        asset.SourceType.Should().Be("uploaded");
        asset.DesignImageId.Should().BeNull();
        asset.MockupTemplateId.Should().BeNull();
        asset.MockupImageUrl.Should().BeNull();
        asset.ContentHash.Should().Be(new string('f', 64));
        asset.MetadataRevision.Should().Be(1);
        asset.ApprovalStatus.Should().Be("pending");
        asset.ApprovedRevision.Should().BeNull();
        asset.ArtworkGroupKey.Should().Be(product.Id.ToString());
        result.Value.PreviewUrl.Should().Be("https://example.invalid/expiring");
        fixture.Unit.Verify(x => x.SaveChangesAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(20971521L)]
    public async Task UploadAsync_DeclaredSizeOutOfRange_DoesNotCallStorage(long length)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        using var stream = new MemoryStream(Png);
        var result = await fixture.MockupService.UploadAsync(product.Id, stream, "mockup.png", length, default);
        result.Error.Message.Should().Be("Images must be at most 20 MB.");
        fixture.Storage.Verify(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.AssetRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UploadAsync_ActualStreamExceedsDeclaredSizeAndLimit_RejectsBeforeUpload()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        using var stream = new MemoryStream(new byte[20 * 1024 * 1024 + 1]);
        var result = await fixture.MockupService.UploadAsync(product.Id, stream, "mockup.png", 100, default);
        result.Error.Message.Should().Be("Image exceeds 20 MB.");
        fixture.AssetRows.Should().BeEmpty();
        fixture.Storage.Verify(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadAsync_ForeignProduct_DoesNotReadOrUploadSource()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct(); product.UserId = Guid.NewGuid();
        using var stream = new MemoryStream(Png);
        var result = await fixture.MockupService.UploadAsync(product.Id, stream, "mockup.png", Png.Length, default);
        result.Error.Code.Should().Be("MockupNotFound");
        stream.Position.Should().Be(0);
        fixture.Storage.Verify(x => x.UploadMockupAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(true, "approved")]
    [DataRow(false, "rejected")]
    public async Task ReviewAsync_CurrentRevision_BindsOrRevokesExactApproval(bool approve, string status)
    {
        var fixture = new WorkflowTestData();
        var asset = fixture.AddAsset(fixture.AddProduct());
        var result = await fixture.MockupService.ReviewAsync(asset.Id, new(3, approve), default);
        result.IsSuccess.Should().BeTrue();
        asset.ApprovalStatus.Should().Be(status);
        asset.ApprovedRevision.Should().Be(approve ? 3L : null);
        asset.ApprovedBy.Should().Be(approve ? WorkflowTestData.Owner : null);
        asset.ApprovedAt.Should().Be(approve ? WorkflowTestData.Now.UtcDateTime : null);
    }

    [TestMethod]
    public async Task ReviewAsync_ForeignAsset_DoesNotApprove()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct(); product.UserId = Guid.NewGuid();
        var asset = fixture.AddAsset(product); asset.ApprovalStatus = "pending";
        var result = await fixture.MockupService.ReviewAsync(asset.Id, new(3, true), default);
        result.Error.Code.Should().Be("MockupNotFound");
        asset.ApprovalStatus.Should().Be("pending");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void IsStaticImage_FormatsAndAnimationChunks_RejectsAnimatedAndTruncatedContent()
    {
        MockupAssetService.IsStaticImage(Png).Should().BeTrue();
        MockupAssetService.IsStaticImage([0xff, 0xd8, 0xff, 0xe0, 0, 0, 0, 0, 0, 0, 0, 0]).Should().BeTrue();
        MockupAssetService.IsStaticImage("GIF89a000000"u8.ToArray()).Should().BeFalse();
        MockupAssetService.IsStaticImage(Png[..20]).Should().BeFalse();
        var apng = new byte[20]; Png.AsSpan(0, 8).CopyTo(apng); "acTL"u8.CopyTo(apng.AsSpan(12));
        MockupAssetService.IsStaticImage(apng).Should().BeFalse();
        foreach (var type in new[] { "ANIM", "ANMF", "VP8X" })
        {
            var bytes = new byte[22]; "RIFF"u8.CopyTo(bytes); "WEBP"u8.CopyTo(bytes.AsSpan(8));
            System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 12);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 1); bytes[20] = 2;
            MockupAssetService.IsStaticImage(bytes).Should().BeFalse(type);
        }
    }
}
