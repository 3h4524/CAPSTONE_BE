using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class EtsyPublishingServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid productId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<IEtsyShopClient> etsy = new();
    private readonly Mock<IRepository<EtsyIntegration>> integrations = new();
    private readonly Mock<IRepository<EtsyUploadLog>> uploads = new();
    private readonly Mock<IMediaStorage> storage = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly List<EtsyIntegration> integrationRows = [];
    private readonly List<EtsyUploadLog> logs = [];
    private MockupImage approved = null!;
    private MockupImage pending = null!;
    private DesignImage design = null!;
    private DesignImage rejectedDesign = null!;

    private EtsyPublishingService Create(bool connected = true)
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        if (connected)
            integrationRows.Add(new EtsyIntegration
            {
                Id = Guid.NewGuid(), UserId = userId, EtsyShopId = "12345", ShopName = "APCSCo", IsActive = true, IsDefault = true,
                EtsyOauthTokenEncrypted = $"enc:{clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()}:access",
                EtsyRefreshTokenEncrypted = "enc:refresh", OauthExpiresAt = clock.GetUtcNow().AddDays(60).UtcDateTime,
            });
        integrations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EtsyIntegration, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<EtsyIntegration, bool>> predicate, CancellationToken _) => integrationRows.Where(predicate.Compile()).ToList());
        integrations.Setup(x => x.Query()).Returns(() => integrationRows.AsQueryable().BuildMock());
        uploads.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EtsyUploadLog, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<EtsyUploadLog, bool>> predicate, CancellationToken _) => logs.Where(predicate.Compile()).ToList());
        uploads.Setup(x => x.Query()).Returns(() => logs.AsQueryable().BuildMock());
        uploads.Setup(x => x.AddAsync(It.IsAny<EtsyUploadLog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((EtsyUploadLog log, bool _, CancellationToken _) => { log.EtsyIntegration = integrationRows[0]; logs.Add(log); });

        etsy.Setup(x => x.Protect(It.IsAny<string>())).Returns((string value) => $"enc:{value}");
        etsy.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value["enc:".Length..]);
        etsy.Setup(x => x.CreateDraftListingAsync("access", "12345", It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyCreatedListing("987", null));
        storage.Setup(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);

        approved = new MockupImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "a", ApprovalStatus = "approved", ApprovedRevision = 2, MetadataRevision = 2, CreatedAt = clock.GetUtcNow().UtcDateTime };
        pending = new MockupImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "b", ApprovalStatus = "pending", MetadataRevision = 1, CreatedAt = clock.GetUtcNow().UtcDateTime };
        var mockups = new Mock<IRepository<MockupImage>>();
        mockups.Setup(x => x.Query()).Returns(() => new[] { approved, pending }.AsQueryable().BuildMock());
        var products = new Mock<IRepository<Product>>();
        products.Setup(x => x.Query()).Returns(() => new[] { new Product { Id = productId, UserId = userId, Name = "Cat mom tee", InputDescription = "tee" } }.AsQueryable().BuildMock());
        design = new DesignImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "design-images/a", ImageUrl = "https://cdn.test/design-a.png", FileFormat = "png", FileSizeMb = 3, ApprovalStatus = "approved", ImageWidthPx = 4500, ImageHeightPx = 5400 };
        rejectedDesign = new DesignImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "design-images/b", ImageUrl = "https://cdn.test/design-b.png", FileFormat = "png", FileSizeMb = 3, ApprovalStatus = "rejected" };
        var designs = new Mock<IRepository<DesignImage>>();
        designs.Setup(x => x.Query()).Returns(() => new[] { design, rejectedDesign }.AsQueryable().BuildMock());
        var videos = new Mock<IRepository<PromoVideo>>();
        videos.Setup(x => x.Query()).Returns(() => Array.Empty<PromoVideo>().AsQueryable().BuildMock());

        var shops = new ShopService(current.Object, Mock.Of<ICacheService>(), etsy.Object, integrations.Object, Mock.Of<IUnitOfWork>(), clock, NullLogger<ShopService>.Instance);
        return new EtsyPublishingService(current.Object, shops, etsy.Object, Mock.Of<IListingContentService>(), products.Object, mockups.Object,
            designs.Object, videos.Object, uploads.Object, integrations.Object, storage.Object, Mock.Of<ICacheService>(), Mock.Of<IUnitOfWork>(), clock,
            new PublishEtsyDraftValidator(), NullLogger<EtsyPublishingService>.Instance);
    }

    private PublishEtsyDraftRequestDto Request(params Guid[] mockupIds) =>
        new("Cat Mom Shirt", "A soft tee.", ["cat mom"], 24.99m, 999, 482, "Clothing > Shirts", "physical", "i_did", "made_to_order",
            5, null, [], mockupIds, false);

    [TestMethod]
    public async Task PublishDraftAsync_AMockupNotApproved_IsRejectedBeforeEtsyIsCalled()
    {
        var service = Create();

        var result = await service.PublishDraftAsync(productId, Request(pending.Id));

        result.Error.Type.Should().Be(ErrorType.Validation);
        etsy.Verify(x => x.CreateDraftListingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PublishDraftAsync_CreatesTheDraftUploadsImagesAndLogsSuccess()
    {
        var service = Create();

        var result = await service.PublishDraftAsync(productId, Request(approved.Id));

        result.Value.ListingId.Should().Be("987");
        result.Value.EditUrl.Should().EndWith("/987");
        result.Value.ImagesUploaded.Should().Be(1);
        etsy.Verify(x => x.UploadListingImageAsync("access", "12345", "987", It.IsAny<byte[]>(), "mockup-1.png", 1, "Cat Mom Shirt", It.IsAny<CancellationToken>()), Times.Once);
        var log = logs.Should().ContainSingle().Subject;
        log.UploadStatus.Should().Be("success");
        log.ListingState.Should().Be("draft");
        log.EtsyListingId.Should().Be("987");
    }

    [TestMethod]
    public async Task PublishDraftAsync_SameContentTwice_ReturnsTheFirstDraft()
    {
        var service = Create();
        await service.PublishDraftAsync(productId, Request(approved.Id));

        var again = await service.PublishDraftAsync(productId, Request(approved.Id));

        again.Value.AlreadyPublished.Should().BeTrue();
        again.Value.ListingId.Should().Be("987");
        etsy.Verify(x => x.CreateDraftListingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PublishDraftAsync_AFailedImage_KeepsTheDraftAndWarns()
    {
        var service = Create();
        etsy.Setup(x => x.UploadListingImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unavailable, "down"));

        var result = await service.PublishDraftAsync(productId, Request(approved.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.ImagesUploaded.Should().Be(0);
        result.Value.Warnings.Should().ContainSingle();
        logs.Single().UploadStatus.Should().Be("success");
    }

    [TestMethod]
    public async Task PublishDraftAsync_EtsyRefusesTheListing_LogsTheFailure()
    {
        var service = Create();
        etsy.Setup(x => x.CreateDraftListingAsync("access", "12345", It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy returned 400."));

        var result = await service.PublishDraftAsync(productId, Request(approved.Id));

        result.Error.Code.Should().Be("shop.publish_failed");
        logs.Single().UploadStatus.Should().Be("failed");
    }

    [TestMethod]
    public async Task PublishDraftAsync_DigitalListing_AttachesTheChosenDesignFile()
    {
        var service = Create();
        var request = Request(approved.Id) with { Type = "download", ShippingProfileId = null, DesignIds = [design.Id] };

        var result = await service.PublishDraftAsync(productId, request);

        result.Value.FilesUploaded.Should().Be(1);
        etsy.Verify(x => x.UploadListingFileAsync("access", "12345", "987", "https://cdn.test/design-a.png", "design-1.png", 1, It.IsAny<CancellationToken>()), Times.Once);
        etsy.Verify(x => x.CreateDraftListingAsync("access", "12345", It.Is<EtsyDraftListing>(draft => draft.Type == "download" && draft.ShippingProfileId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PublishDraftAsync_ARejectedDesign_IsNotSentAsAFile()
    {
        var service = Create();
        var request = Request(approved.Id) with { Type = "download", ShippingProfileId = null, DesignIds = [rejectedDesign.Id] };

        var result = await service.PublishDraftAsync(productId, request);

        result.Error.Type.Should().Be(ErrorType.Validation);
        etsy.Verify(x => x.CreateDraftListingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PublishDraftAsync_EtsyRefusal_PassesEtsysReasonToTheSeller()
    {
        var service = Create();
        etsy.Setup(x => x.CreateDraftListingAsync("access", "12345", It.IsAny<EtsyDraftListing>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy returned 400.", "Price must be at least 0.20 USD"));

        var result = await service.PublishDraftAsync(productId, Request(approved.Id));

        result.Error.Message.Should().Contain("Price must be at least 0.20 USD");
    }

    [TestMethod]
    public async Task PublishDraftAsync_NotConnected_AsksToReconnect()
    {
        var service = Create(connected: false);
        (await service.PublishDraftAsync(productId, Request(approved.Id))).Error.Code.Should().Be("shop.reconnect_required");
    }
}
