using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class ListingPaceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<ICacheService> cache = new();
    private readonly List<EtsyUploadLog> etsyRows = [];
    private readonly List<PrintifyUploadLog> printifyRows = [];

    private ListingPace Create(DateTimeOffset? openedAt)
    {
        cache.Setup(x => x.GetAsync<ListingPace.ShopOpened>(ListingPace.OpenedKey(userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingPace.ShopOpened(openedAt));
        var etsyLogs = new Mock<IRepository<EtsyUploadLog>>();
        etsyLogs.Setup(x => x.Query()).Returns(() => etsyRows.AsQueryable().BuildMock());
        var printifyLogs = new Mock<IRepository<PrintifyUploadLog>>();
        printifyLogs.Setup(x => x.Query()).Returns(() => printifyRows.AsQueryable().BuildMock());
        var shops = new ShopService(Mock.Of<ICurrentUser>(), cache.Object, Mock.Of<IEtsyShopClient>(), Mock.Of<IRepository<EtsyIntegration>>(),
            Mock.Of<IUnitOfWork>(), clock, NullLogger<ShopService>.Instance);
        return new ListingPace(shops, Mock.Of<IEtsyShopClient>(), cache.Object, etsyLogs.Object, printifyLogs.Object, clock, NullLogger<ListingPace>.Instance);
    }

    private void ListedDirectly(params int[] minutesAgo)
    {
        var integration = new EtsyIntegration { Id = Guid.NewGuid(), UserId = userId, EtsyShopId = "1", ShopName = "Shop" };
        foreach (var ago in minutesAgo)
            etsyRows.Add(new EtsyUploadLog { Id = Guid.NewGuid(), EtsyIntegration = integration, EtsyListingId = "l", AttemptedAt = clock.GetUtcNow().UtcDateTime.AddMinutes(-ago) });
    }

    private void PublishedThroughPrintify(params int[] minutesAgo) => AddPrintify(true, minutesAgo);

    private void MadeOnPrintifyOnly(params int[] minutesAgo) => AddPrintify(false, minutesAgo);

    private void AddPrintify(bool published, int[] minutesAgo)
    {
        var integration = new PrintifyIntegration { Id = Guid.NewGuid(), UserId = userId };
        foreach (var ago in minutesAgo)
        {
            var at = clock.GetUtcNow().UtcDateTime.AddMinutes(-ago);
            printifyRows.Add(new PrintifyUploadLog
            {
                Id = Guid.NewGuid(), PrintifyIntegration = integration, SyncType = "create", UploadStatus = "success", CompletedAt = at,
                ApiResponse = new PrintifyService.LogState(published ? PrintifyStages.Publishing : PrintifyStages.Created, null,
                    published ? at : null, 0, false).Write(),
            });
        }
    }

    [TestMethod]
    public void Rules_GrowWithTheShopsAge_AndAShopOfUnknownAgeCountsAsNew()
    {
        var now = clock.GetUtcNow();
        ListingPaceRules.For(null, now).PerDay.Should().Be(10);
        ListingPaceRules.For(now.AddDays(-10), now).Should().Be(new ListingPaceLimit(3, 10, "under 30 days old"));
        ListingPaceRules.For(now.AddDays(-45), now).PerDay.Should().Be(25);
        ListingPaceRules.For(now.AddDays(-400), now).PerDay.Should().Be(100);
    }

    [TestMethod]
    public async Task CheckAsync_ANewShopAtItsHourlyPace_WaitsUntilTheOldestOfThemIsAnHourOld()
    {
        var pace = Create(clock.GetUtcNow().AddDays(-3));
        ListedDirectly(10, 40);
        PublishedThroughPrintify(50);

        var decision = await pace.CheckAsync(userId, default);

        decision.Allowed.Should().BeFalse();
        decision.RetryAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime.AddMinutes(10));
        decision.Reason.Should().Contain("3 listings an hour");
    }

    [TestMethod]
    public async Task CheckAsync_ProductsOnlyMadeOnPrintify_AddNoListingAndDoNotCount()
    {
        var pace = Create(clock.GetUtcNow().AddDays(-3));
        MadeOnPrintifyOnly(10, 20, 29);

        (await pace.CheckAsync(userId, default)).Allowed.Should().BeTrue();
    }

    [TestMethod]
    public async Task CheckAsync_ANewShopAtItsDailyPace_WaitsADayFromTheOldestCounted()
    {
        var pace = Create(clock.GetUtcNow().AddDays(-3));
        ListedDirectly(1300, 1250, 1200, 1100, 1000, 900, 800, 700, 600, 500);

        var decision = await pace.CheckAsync(userId, default);

        decision.Allowed.Should().BeFalse();
        decision.RetryAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime.AddMinutes(-1300).AddDays(1));
    }

    [TestMethod]
    public async Task CheckAsync_AnEstablishedShop_KeepsListing_AndOlderListingsDoNotCount()
    {
        var pace = Create(clock.GetUtcNow().AddYears(-2));
        ListedDirectly(10, 20, 30, 2000, 3000);

        (await pace.CheckAsync(userId, default)).Allowed.Should().BeTrue();
    }

    [TestMethod]
    public void IsAiMade_TellsAiPhotosFromTemplateMockups()
    {
        ListingPhotos.IsAiMade(new MockupImage { SourceType = MockupSources.Generated, Role = "Hero", MockupTemplate = new MockupTemplate() }).Should().BeFalse();
        ListingPhotos.IsAiMade(new MockupImage { SourceType = MockupSources.Uploaded, Role = "Hero" }).Should().BeFalse();
        ListingPhotos.IsAiMade(new MockupImage { SourceType = MockupSources.AiGenerated, Role = "Hero" }).Should().BeTrue();
        ListingPhotos.IsAiMade(new MockupImage { SourceType = MockupSources.Uploaded, Role = "Lifestyle" }).Should().BeTrue();
        ListingPhotos.IsAiMade(new MockupImage { SourceType = MockupSources.Generated, Role = "Lifestyle", MockupTemplate = new MockupTemplate { AiGenerated = true } })
            .Should().BeTrue();
    }
}
