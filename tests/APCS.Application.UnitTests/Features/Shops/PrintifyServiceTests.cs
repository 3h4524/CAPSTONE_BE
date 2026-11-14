using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
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
public sealed class PrintifyServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid productId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<IPrintifyClient> printify = new();
    private readonly Mock<IEtsyShopClient> etsy = new();
    private readonly Mock<IRepository<PrintifyIntegration>> integrations = new();
    private readonly Mock<IRepository<PrintifyUploadLog>> uploads = new();
    private readonly Mock<IRepository<EtsyIntegration>> etsyIntegrations = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly Mock<IMediaStorage> storage = new();
    private readonly Mock<IPrintFileSource> printFiles = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly List<PrintifyIntegration> integrationRows = [];
    private readonly List<ApiKey> keyRows = [];
    private readonly Mock<IApiKeyRepository> keys = new();
    private readonly Mock<IApiKeyCredentials> credentials = new();
    private readonly List<PrintifyUploadLog> logs = [];
    private readonly List<EtsyIntegration> etsyRows = [];
    private DesignImage design = null!;
    private MockupImage mockup = null!;
    /// <summary>Approved mock-ups beside <see cref="mockup"/>, such as the other sides of its garment.</summary>
    private readonly List<MockupImage> moreMockups = [];

    private static readonly PrintifyCatalogVariant[] Variants =
    [
        new(101, "White / S", "White", "S", [new("front", 4500, 5100)]),
        new(102, "White / M", "White", "M", [new("front", 4500, 5100)]),
        new(103, "Black / S", "Black", "S", [new("front", 4500, 5100)]),
    ];

    private PrintifyService Create(bool connected = true, bool keyChecked = true)
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        // The Printify key is the seller's row on the API Keys page.
        if (connected)
            keyRows.Add(new ApiKey
            {
                Id = Guid.NewGuid(), UserId = userId, ServiceProvider = "printify", KeyIdentifier = "My Printify", KeyValueEncrypted = "enc:token",
                AuthType = "api_key", IsActive = true, LastCheckSucceeded = keyChecked, LastCheckedAt = clock.GetUtcNow().UtcDateTime, CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
        keys.Setup(x => x.ListOwnedAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(() => keyRows.ToList());
        credentials.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value["enc:".Length..]);
        cache.Setup(x => x.GetAsync<PrintifyShop>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PrintifyShop?)null);
        printify.Setup(x => x.GetShopsAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyShop(5, "Other", "disconnected"), new PrintifyShop(777, "APCSCo", "etsy")]);
        integrations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<PrintifyIntegration, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<PrintifyIntegration, bool>> predicate, CancellationToken _) => integrationRows.Where(predicate.Compile()).ToList());
        integrations.Setup(x => x.AddAsync(It.IsAny<PrintifyIntegration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((PrintifyIntegration row, bool _, CancellationToken _) => integrationRows.Add(row));
        uploads.Setup(x => x.FindAsync(It.IsAny<Expression<Func<PrintifyUploadLog, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<PrintifyUploadLog, bool>> predicate, CancellationToken _) => logs.Where(predicate.Compile()).ToList());
        uploads.Setup(x => x.Query()).Returns(() => logs.AsQueryable().BuildMock());
        uploads.Setup(x => x.AddAsync(It.IsAny<PrintifyUploadLog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((PrintifyUploadLog row, bool _, CancellationToken _) => { row.PrintifyIntegration = integrationRows[0]; logs.Add(row); });

        cache.Setup(x => x.GetAsync<PrintifyCatalogVariant[]>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PrintifyCatalogVariant[]?)null);
        cache.Setup(x => x.GetAsync<PrintifyShippingRate[]>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PrintifyShippingRate[]?)null);
        printify.Setup(x => x.GetVariantsAsync("token", 6, 99, It.IsAny<CancellationToken>())).ReturnsAsync(Variants);
        printify.Setup(x => x.GetShippingAsync("token", 6, 99, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PrintifyShippingRate([101, 102, 103], ["US"], 475, 240, "USD")]);
        printify.Setup(x => x.UploadImageAsync("token", "design-1.png", "https://cdn.test/design.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintifyUploadedImage("img-1", 4500, 5400));
        printify.Setup(x => x.CreateProductAsync("token", 777, It.IsAny<PrintifyNewProduct>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Product(external: null));

        design = new DesignImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "design-images/a", ImageUrl = "https://cdn.test/design.png", FileFormat = "png", ApprovalStatus = "approved", ImageWidthPx = 4500, ImageHeightPx = 5400 };
        mockup = new MockupImage { Id = Guid.NewGuid(), ProductId = productId, StorageKey = "m", ApprovalStatus = "approved", ApprovedRevision = 1, MetadataRevision = 1, CreatedAt = clock.GetUtcNow().UtcDateTime };
        var designs = new Mock<IRepository<DesignImage>>();
        designs.Setup(x => x.Query()).Returns(() => new[] { design }.AsQueryable().BuildMock());
        var mockups = new Mock<IRepository<MockupImage>>();
        mockups.Setup(x => x.Query()).Returns(() => new[] { mockup }.Concat(moreMockups).AsQueryable().BuildMock());
        var products = new Mock<IRepository<Product>>();
        products.Setup(x => x.Query()).Returns(() => new[] { new Product { Id = productId, UserId = userId, Name = "Cat tee", InputDescription = "tee" } }.AsQueryable().BuildMock());
        storage.Setup(x => x.ReadImageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);

        // Etsy is connected too, so APCS media can be added to Printify's listing.
        etsyRows.Add(new EtsyIntegration
        {
            Id = Guid.NewGuid(), UserId = userId, EtsyShopId = "12345", ShopName = "APCSCo", IsActive = true,
            EtsyOauthTokenEncrypted = $"enc:{clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds()}:access",
            EtsyRefreshTokenEncrypted = "enc:refresh", OauthExpiresAt = clock.GetUtcNow().AddDays(60).UtcDateTime,
        });
        etsyIntegrations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EtsyIntegration, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<EtsyIntegration, bool>> predicate, CancellationToken _) => etsyRows.Where(predicate.Compile()).ToList());
        etsyIntegrations.Setup(x => x.Query()).Returns(() => etsyRows.AsQueryable().BuildMock());
        etsy.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value["enc:".Length..]);

        var shops = new ShopService(current.Object, Mock.Of<ICacheService>(), etsy.Object, etsyIntegrations.Object, Mock.Of<IUnitOfWork>(), clock, NullLogger<ShopService>.Instance);
        return new PrintifyService(current.Object, printify.Object, keys.Object, credentials.Object, shops, etsy.Object, Mock.Of<IListingContentService>(), products.Object,
            designs.Object, mockups.Object, integrations.Object, uploads.Object, storage.Object, cache.Object, Mock.Of<IUnitOfWork>(), clock,
            new CreatePrintifyProductValidator(), new PublishPrintifyProductValidator(), NullLogger<PrintifyService>.Instance, printFiles.Object);
    }

    private static PrintifyProduct Product(string? external, bool locked = false) => new("p-1", "Cat Tee", locked, external, null,
        [
            new PrintifyProductVariant(101, "White / S", 1010, 2499, true, true),
            new PrintifyProductVariant(102, "White / M", 1010, 2499, true, true),
            new PrintifyProductVariant(103, "Black / S", 1159, 2499, false, true),
        ], []);

    private CreatePrintifyProductRequestDto CreateRequest() =>
        new(design.Id, 6, "Unisex Softstyle T-Shirt", 99, "Monster Digital", [101, 102], "Cat Tee", "A soft tee.", ["cat"], 24.99m);

    [TestMethod]
    public async Task GetConnectionAsync_UsesTheKeyFromApiKeys_AndTheStoreLinkedToEtsy()
    {
        var service = Create();

        var result = await service.GetConnectionAsync(refresh: false);

        result.Value.Status.Should().Be(PrintifyStatuses.Connected);
        result.Value.ShopId.Should().Be(777);
        result.Value.SalesChannel.Should().Be("etsy");
        result.Value.KeyName.Should().Be("My Printify");
        // The store is recorded for upload logs; the key itself is not copied out of api_keys.
        var row = integrationRows.Should().ContainSingle().Subject;
        row.PrintifyStoreId.Should().Be("777");
        row.PrintifyApiTokenEncrypted.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetConnectionAsync_NoKeyOnApiKeys_IsNotConnected()
    {
        var service = Create(connected: false);

        (await service.GetConnectionAsync(refresh: false)).Value.Status.Should().Be(PrintifyStatuses.NotConnected);
        printify.Verify(x => x.GetShopsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetConnectionAsync_AKeyThatFailedItsCheck_IsNotUsed()
    {
        var service = Create(keyChecked: false);

        (await service.GetConnectionAsync(refresh: false)).Value.Status.Should().Be(PrintifyStatuses.KeyInvalid);
        printify.Verify(x => x.GetShopsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetConnectionAsync_PrintifyRefusesTheKey_SendsTheSellerToApiKeys()
    {
        var service = Create();
        printify.Setup(x => x.GetShopsAsync("token", It.IsAny<CancellationToken>())).ThrowsAsync(new PrintifyApiException(PrintifyFailureKind.Unauthorized, "401"));

        (await service.GetConnectionAsync(refresh: true)).Value.Status.Should().Be(PrintifyStatuses.KeyInvalid);
        (await service.CreateProductAsync(productId, CreateRequest())).Error.Code.Should().Be("shop.printify_key_invalid");
    }
    [TestMethod]
    public async Task CreateProductAsync_SendsEveryVariantButSellsOnlyTheChosen_AndReportsProfit()
    {
        var service = Create();

        var result = await service.CreateProductAsync(productId, CreateRequest());

        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            product.ImageId == "img-1" && product.Position == "front" && product.Variants.Count == 3
            && product.Variants.Count(variant => variant.IsEnabled) == 2 && product.Variants.All(variant => variant.PriceCents == 2499)),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Value.State.Should().Be(PrintifyStages.Created);
        result.Value.Variants.Should().HaveCount(2);
        var first = result.Value.Variants[0];
        first.Cost.Should().Be(10.10m);
        first.EstimatedProfit.Should().Be(24.99m - 10.10m - PrintifyPricing.EstimatedFees(24.99m, 4.75m));
        result.Value.UsShippingFirstItem.Should().Be(4.75m);
        logs.Should().ContainSingle().Which.PrintifyProductId.Should().Be("p-1");
        integrationRows[0].TotalProductsUploaded.Should().Be(1);
    }

    [TestMethod]
    public async Task CreateProductAsync_SameSettingsTwice_ReturnsTheFirstProduct()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: null));
        await service.CreateProductAsync(productId, CreateRequest());

        var again = await service.CreateProductAsync(productId, CreateRequest());

        again.Value.AlreadyCreated.Should().BeTrue();
        printify.Verify(x => x.CreateProductAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<PrintifyNewProduct>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateProductAsync_PrintifyRefusesTheImage_ShowsItsReasonAndLogsTheFailure()
    {
        var service = Create();
        printify.Setup(x => x.CreateProductAsync("token", 777, It.IsAny<PrintifyNewProduct>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PrintifyApiException(PrintifyFailureKind.Rejected, "400", "Image has low quality"));

        var result = await service.CreateProductAsync(productId, CreateRequest());

        result.Error.Message.Should().Contain("Image has low quality");
        logs.Single().UploadStatus.Should().Be("failed");
    }

    [TestMethod]
    public async Task PublishAsync_APriceBelowCost_IsRefusedBeforePrintifyPublishes()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: null));
        await service.CreateProductAsync(productId, CreateRequest());

        var result = await service.PublishAsync(productId, "p-1", new PublishPrintifyProductRequestDto([new(101, 9.99m), new(102, 24.99m)]));

        result.Error.Type.Should().Be(ErrorType.Validation);
        printify.Verify(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PublishAsync_SetsPricesSellsOnlyPricedVariants_AndStartsPublishing()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: null, locked: true));
        await service.CreateProductAsync(productId, CreateRequest());

        var result = await service.PublishAsync(productId, "p-1", new PublishPrintifyProductRequestDto([new(101, 24.99m), new(102, 26.99m)]));

        printify.Verify(x => x.UpdatePricesAsync("token", 777, "p-1", It.Is<IReadOnlyList<PrintifyVariantPrice>>(prices =>
            prices.Single(price => price.Id == 102).PriceCents == 2699 && !prices.Single(price => price.Id == 103).IsEnabled), It.IsAny<CancellationToken>()), Times.Once);
        printify.Verify(x => x.PublishAsync("token", 777, "p-1", It.IsAny<CancellationToken>()), Times.Once);
        result.Value.State.Should().Be(PrintifyStages.Publishing);
    }

    [TestMethod]
    public async Task PublishAsync_AShopWithoutAProductionPartner_IsRefusedBeforePrintifyPublishes()
    {
        var service = Create();
        etsy.Setup(x => x.GetProductionPartnersAsync("access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await service.CreateProductAsync(productId, CreateRequest());

        var result = await service.PublishAsync(productId, "p-1", new PublishPrintifyProductRequestDto([new(101, 24.99m)]));

        result.Error.Message.Should().Contain("production partner");
        printify.Verify(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateProductAsync_TheDescriptionSaysTheDesignWasMadeWithAi()
    {
        var service = Create();

        await service.CreateProductAsync(productId, CreateRequest());

        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            product.Description == $"A soft tee.\n\n{EtsyListingText.AiDisclosure}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SyncAsync_OnceOnEtsy_AddsTheMockupsToTheListingOnlyOnce()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: "4455"));
        await service.CreateProductAsync(productId, CreateRequest());
        var sync = new SyncPrintifyProductRequestDto([mockup.Id]);

        var first = await service.SyncAsync(productId, "p-1", sync);
        var second = await service.SyncAsync(productId, "p-1", sync);

        first.Value.State.Should().Be(PrintifyStages.Published);
        first.Value.EtsyUrl.Should().Be("https://www.etsy.com/listing/4455");
        first.Value.MediaAttached.Should().Be(1);
        second.Value.MediaAttached.Should().Be(1);
        etsy.Verify(x => x.UploadListingImageAsync("access", "12345", "4455", It.IsAny<byte[]>(), It.IsAny<string>(), 1, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SyncAsync_UnlockedWithoutAListingLongAfterPublishing_TellsTheSellerToCheckPrintify()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: null));
        await service.CreateProductAsync(productId, CreateRequest());
        await service.PublishAsync(productId, "p-1", new PublishPrintifyProductRequestDto([new(101, 24.99m)]));
        clock.Advance(PrintifyService.PublishGrace + TimeSpan.FromMinutes(1));

        var result = await service.SyncAsync(productId, "p-1", new SyncPrintifyProductRequestDto([]));

        result.Value.State.Should().Be(PrintifyStages.Created);
        result.Value.Warnings.Should().ContainSingle().Which.Should().Contain("without creating the Etsy listing");
    }

    [TestMethod]
    public async Task SyncAsync_AProductNotMadeFromThisProduct_IsNotFound()
    {
        var service = Create();

        var result = await service.SyncAsync(productId, "someone-elses", new SyncPrintifyProductRequestDto([]));

        result.Error.Code.Should().Be("shop.printify_product_not_found");
    }

    /// <summary>A mock-up of the design on a template whose print area is 400 × 500 at (100, 100), made by a job.</summary>
    private void ShownOnTemplate(string? box)
    {
        var templateId = Guid.NewGuid();
        mockup.DesignImageId = design.Id;
        mockup.MockupTemplateId = templateId;
        mockup.GarmentColor = "#FFFFFF";
        mockup.MockupTemplate = new MockupTemplate
        {
            Id = templateId, Name = "Tee", ProductType = "tshirt", BaseImageUrl = "b", Kind = "garment",
            PrintAreaConfig = """{"x":100,"y":100,"width":400,"height":500,"unit":"px"}""",
        };
        var slot = $"{design.Id:N}.{templateId:N}.FFFFFF";
        var config = box is null
            ? "{}"
            : "{\"mockupOverrides\":{\"" + slot + "\":{\"mockupTemplateId\":\"" + templateId + "\",\"garmentColor\":\"#FFFFFF\",\"printArea\":" + box + "}}}";
        mockup.BatchJobProduct = new BatchJobProduct { BatchJob = new BatchJob { Config = config } };
    }

    [TestMethod]
    public async Task CreateProductAsync_FollowingAMockup_PrintsTheDesignWhereTheSellerMovedIt()
    {
        var service = Create();
        // Half the template's print area wide, centered across it and 30% down it.
        ShownOnTemplate("""{"x":200,"y":125,"width":200,"height":250}""");

        var result = await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = mockup.Id });

        result.IsSuccess.Should().BeTrue();
        // As large as fits the 4500 × 5100 area is 5100/5400 of its width; half of that.
        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            product.X == 0.5 && product.Y == 0.3 && Math.Abs(product.Scale - 0.4722) < 0.0001), It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// A back photo of the template <see cref="ShownOnTemplate"/> made, linked as its back side, with an approved mock-up
    /// of the design moved to the top half of its print area.
    /// </summary>
    private MockupImage BackSideShown()
    {
        var front = mockup.MockupTemplate!;
        var backId = Guid.NewGuid();
        front.PrintAreaConfig = """{"x":100,"y":100,"width":400,"height":500,"unit":"px","views":{"back":"BACK"}}""".Replace("BACK", backId.ToString());
        var back = new MockupImage
        {
            Id = Guid.NewGuid(), ProductId = productId, StorageKey = "b", DesignImageId = design.Id, ApprovalStatus = "approved", ApprovedRevision = 1,
            MetadataRevision = 1, CreatedAt = clock.GetUtcNow().UtcDateTime.AddMinutes(-1), MockupTemplateId = backId, GarmentColor = "#FFFFFF",
            MockupTemplate = new MockupTemplate
            {
                Id = backId, Name = "Tee · Back", ProductType = "tshirt", BaseImageUrl = "bb", Kind = "garment",
                PrintAreaConfig = """{"x":100,"y":100,"width":400,"height":500,"unit":"px","view":"back","mainTemplateId":"MAIN"}""".Replace("MAIN", front.Id.ToString()),
            },
        };
        var slot = $"{design.Id:N}.{backId:N}.FFFFFF";
        back.BatchJobProduct = new BatchJobProduct
        {
            BatchJob = new BatchJob { Config = "{\"mockupOverrides\":{\"" + slot + "\":{\"mockupTemplateId\":\"" + backId + "\",\"garmentColor\":\"#FFFFFF\",\"printArea\":{\"x\":200,\"y\":100,\"width\":200,\"height\":250}}}}" },
        };
        moreMockups.Add(back);
        return back;
    }

    [TestMethod]
    public async Task CreateProductAsync_FollowingTheMockups_PrintsTheBackTheyShow_AndReadsTheFrontEvenFromABackMockup()
    {
        var service = Create();
        printify.Setup(x => x.GetVariantsAsync("token", 6, 99, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PrintifyCatalogVariant(101, "White / S", "White", "S", [new("front", 4500, 5100), new("back", 4500, 5100)]),
            new PrintifyCatalogVariant(102, "White / M", "White", "M", [new("front", 4500, 5100), new("back", 4500, 5100)]),
        ]);
        ShownOnTemplate("""{"x":200,"y":125,"width":200,"height":250}""");
        var back = BackSideShown();

        // The back mock-up comes first in the listing; the print still follows the front.
        var result = await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = back.Id, PrintMockupSides = true });

        result.IsSuccess.Should().BeTrue();
        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            product.Position == "front" && product.X == 0.5 && product.Y == 0.3
            && product.MorePlacements != null && product.MorePlacements.Count == 1
            && product.MorePlacements[0].Position == "back" && product.MorePlacements[0].X == 0.5 && product.MorePlacements[0].Y == 0.25),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateProductAsync_AProviderThatDoesNotPrintTheBack_PrintsTheFrontOnly_AndSaysSo()
    {
        var service = Create();
        ShownOnTemplate(box: null);
        BackSideShown();

        var result = await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = mockup.Id, PrintMockupSides = true });

        result.IsSuccess.Should().BeTrue();
        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product => product.MorePlacements == null),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().ContainSingle(warning => warning.Contains("does not print the back"));
    }

    [TestMethod]
    public async Task CreateProductAsync_WithoutPrintingSides_IgnoresTheBackMockups()
    {
        var service = Create();
        ShownOnTemplate(box: null);
        BackSideShown();

        await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = mockup.Id });

        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product => product.MorePlacements == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateProductAsync_FollowingAMockupNobodyMoved_FitsTheDesignToThePrintArea()
    {
        var service = Create();
        ShownOnTemplate(box: null);

        await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = mockup.Id });

        // The design is taller than the area, so full width (scale 1) would be cut off at the bottom.
        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            product.X == 0.5 && product.Y == 0.5 && Math.Abs(product.Scale - 0.9444) < 0.0001), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateProductAsync_AMockupBoxPastThePrintArea_IsKeptInsideIt_AndSaysSo()
    {
        var service = Create();
        // Wider than the template's print area and pushed past its right edge.
        ShownOnTemplate("""{"x":300,"y":100,"width":600,"height":750}""");

        var result = await service.CreateProductAsync(productId, CreateRequest() with { PlacementMockupId = mockup.Id });

        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product =>
            Math.Abs(product.Scale - 0.9444) < 0.0001 && product.X <= 1 - product.Scale / 2 + 0.0001), It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().ContainSingle().Which.Should().Contain("outside Printify's print area");
    }

    [TestMethod]
    public async Task CreateProductAsync_ASmallDesign_IsSentEnlargedToItsPrintWidth_AndWarnsOfASoftPrint()
    {
        var service = Create();
        design.ImageWidthPx = 1500;
        design.ImageHeightPx = 1800;
        printFiles.Setup(x => x.Resized("https://cdn.test/design.png", 1500, 1800, 4250)).Returns("https://cdn.test/print.png");
        printify.Setup(x => x.UploadImageAsync("token", "design-1.png", "https://cdn.test/print.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintifyUploadedImage("img-big", 4250, 5100));

        var result = await service.CreateProductAsync(productId, CreateRequest() with { Scale = 0.9444 });

        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product => product.ImageId == "img-big"), It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().ContainSingle().Which.Should().Contain("about 106 DPI").And.Contain("enlarged");
    }

    [TestMethod]
    public async Task CreateProductAsync_AnEnlargedFilePrintifyCannotFetch_SendsTheOriginal()
    {
        var service = Create();
        design.ImageWidthPx = 1500;
        design.ImageHeightPx = 1800;
        printFiles.Setup(x => x.Resized(It.IsAny<string>(), 1500, 1800, It.IsAny<int>())).Returns("https://cdn.test/print.png");
        printify.Setup(x => x.UploadImageAsync("token", "design-1.png", "https://cdn.test/print.png", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PrintifyApiException(PrintifyFailureKind.Rejected, "400", "Could not download"));

        var result = await service.CreateProductAsync(productId, CreateRequest());

        result.IsSuccess.Should().BeTrue();
        printify.Verify(x => x.CreateProductAsync("token", 777, It.Is<PrintifyNewProduct>(product => product.ImageId == "img-1"), It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().ContainSingle().Which.Should().NotContain("enlarged");
    }

    [TestMethod]
    public async Task CreateProductAsync_ALargeEnoughDesign_IsSentAsItIs()
    {
        var service = Create();

        var result = await service.CreateProductAsync(productId, CreateRequest());

        printFiles.Verify(x => x.Resized(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        result.Value.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SyncAsync_AfterPrintifysPhoto_AddsTheMockupsBehindIt()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: "4455"));
        etsy.Setup(x => x.GetProductionPartnersAsync("access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync([new EtsyOption(1, "Printify")]);
        await service.CreateProductAsync(productId, CreateRequest());

        var result = await service.SyncAsync(productId, "p-1", new SyncPrintifyProductRequestDto([mockup.Id], AfterPrintifyPhoto: true));

        etsy.Verify(x => x.UploadListingImageAsync("access", "12345", "4455", It.IsAny<byte[]>(), It.IsAny<string>(), 2, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Value.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SyncAsync_AShopWithoutAProductionPartner_IsToldOnceTheListingExists()
    {
        var service = Create();
        printify.Setup(x => x.GetProductAsync("token", 777, "p-1", It.IsAny<CancellationToken>())).ReturnsAsync(Product(external: "4455"));
        etsy.Setup(x => x.GetProductionPartnersAsync("access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await service.CreateProductAsync(productId, CreateRequest());
        var sync = new SyncPrintifyProductRequestDto([]);

        var first = await service.SyncAsync(productId, "p-1", sync);
        var second = await service.SyncAsync(productId, "p-1", sync);

        first.Value.Warnings.Should().ContainSingle().Which.Should().Be(PrintifyService.NoProductionPartner);
        second.Value.Warnings.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateProductAsync_NotConnected_AsksToConnect()
    {
        var service = Create(connected: false);
        (await service.CreateProductAsync(productId, CreateRequest())).Error.Code.Should().Be("shop.printify_not_connected");
    }

    [TestMethod]
    public void EstimatedFees_FollowEtsysUsRates()
    {
        // (24.99 + 4.75) × 9.5% + 0.25 + 0.20 = 3.2753 → 3.28
        PrintifyPricing.EstimatedFees(24.99m, 4.75m).Should().Be(3.28m);
        PrintifyPricing.ToCents(26.995m).Should().Be(2700);
    }

    [TestMethod]
    public void RankBlueprints_PrefersTitlesWithEveryWord()
    {
        var ranked = PrintifyService.RankBlueprints(
        [
            new PrintifyBlueprint(1, "Kids Heavy Cotton Tee", "Gildan", "5000B", null),
            new PrintifyBlueprint(2, "Unisex Softstyle T-Shirt", "Gildan", "64000", null),
            new PrintifyBlueprint(3, "Mug 11oz", "Generic", "Mug", null),
        ], "gildan 64000");

        ranked.Should().ContainSingle().Which.Id.Should().Be(2);
    }
}
