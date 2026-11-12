using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class ShopServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly Mock<IEtsyShopClient> etsy = new();
    private readonly Mock<IRepository<EtsyIntegration>> integrations = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly List<EtsyIntegration> rows = [];

    private static readonly EtsyShopDetails Shop = new(
        "12345", "WildFigStudio", "Handmade tees", "https://www.etsy.com/shop/WildFigStudio", null, "USD",
        42, 0, 310, 1200, 4.9m, 260, false, null);

    private ShopService Create()
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        etsy.SetupGet(x => x.IsConfigured).Returns(true);
        etsy.Setup(x => x.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string state, string challenge) => $"https://www.etsy.com/oauth/connect?state={state}&code_challenge={challenge}");
        etsy.Setup(x => x.BuildReturnUrl(It.IsAny<string>())).Returns((string outcome) => $"http://localhost:3000/shop?etsy={outcome}");
        // Tokens are "encrypted" by a visible prefix, so the tests can read what was stored.
        etsy.Setup(x => x.Protect(It.IsAny<string>())).Returns((string value) => $"enc:{value}");
        etsy.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value["enc:".Length..]);
        integrations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<EtsyIntegration, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<EtsyIntegration, bool>> predicate, CancellationToken _) => rows.Where(predicate.Compile()).ToList());
        integrations.Setup(x => x.AddAsync(It.IsAny<EtsyIntegration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((EtsyIntegration row, bool _, CancellationToken _) => rows.Add(row));
        return new ShopService(current.Object, cache.Object, etsy.Object, integrations.Object, unitOfWork.Object, clock, NullLogger<ShopService>.Instance);
    }

    private void Pending(string state) =>
        cache.Setup(x => x.GetAsync<ShopService.PendingEtsyConnection>(ShopService.StateKey(state), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShopService.PendingEtsyConnection(userId, "verifier"));

    private EtsyIntegration Connected(DateTimeOffset accessExpiresAt, DateTimeOffset? reconnectBy = null)
    {
        var row = new EtsyIntegration
        {
            Id = Guid.NewGuid(), UserId = userId, EtsyShopId = "12345", ShopName = "WildFigStudio", IsActive = true, IsDefault = true,
            EtsyOauthTokenEncrypted = $"enc:{accessExpiresAt.ToUnixTimeSeconds()}:old-access",
            EtsyRefreshTokenEncrypted = "enc:old-refresh",
            OauthExpiresAt = (reconnectBy ?? clock.GetUtcNow().AddDays(60)).UtcDateTime,
            CreatedAt = clock.GetUtcNow().AddDays(-30).UtcDateTime,
        };
        rows.Add(row);
        return row;
    }

    [TestMethod]
    public async Task StartEtsyConnectionAsync_StoresTheVerifierUnderAOneTimeState()
    {
        var service = Create();
        ShopService.PendingEtsyConnection? stored = null;
        string? key = null;
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<ShopService.PendingEtsyConnection>(), ShopService.StateLifetime, It.IsAny<CancellationToken>()))
            .Callback((string k, ShopService.PendingEtsyConnection value, TimeSpan? _, CancellationToken _) => (key, stored) = (k, value));

        var result = await service.StartEtsyConnectionAsync();

        result.IsSuccess.Should().BeTrue();
        stored!.UserId.Should().Be(userId);
        var state = key!["etsy:oauth:state:".Length..];
        // The page receives the S256 challenge of the stored verifier, never the verifier itself.
        result.Value.AuthorizationUrl.Should().Contain($"state={state}")
            .And.Contain($"code_challenge={ShopService.CodeChallenge(stored.CodeVerifier)}")
            .And.NotContain(stored.CodeVerifier);
    }

    [TestMethod]
    public async Task StartEtsyConnectionAsync_WithoutAnEtsyKey_Fails()
    {
        var service = Create();
        etsy.SetupGet(x => x.IsConfigured).Returns(false);
        (await service.StartEtsyConnectionAsync()).Error.Code.Should().Be("shop.etsy_not_configured");
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_UnknownState_IsExpiredAndCallsNoEtsy()
    {
        var service = Create();

        var result = await service.CompleteEtsyConnectionAsync("code", "forged", null);

        result.Outcome.Should().Be(EtsyConnectionOutcomes.Expired);
        etsy.Verify(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_SellerDeclined_IsDeniedAndTheStateIsSpent()
    {
        var service = Create();
        Pending("s1");

        var result = await service.CompleteEtsyConnectionAsync(null, "s1", "access_denied");

        result.Outcome.Should().Be(EtsyConnectionOutcomes.Denied);
        result.RedirectUrl.Should().EndWith("?etsy=denied");
        cache.Verify(x => x.RemoveAsync(ShopService.StateKey("s1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_AShopAnotherAccountConnected_IsRefused()
    {
        var service = Create();
        Pending("s1");
        rows.Add(new EtsyIntegration { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), EtsyShopId = "12345", ShopName = "WildFigStudio", IsActive = true });
        etsy.Setup(x => x.ExchangeCodeAsync("code", "verifier", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyTokenSet("access", "refresh", clock.GetUtcNow().AddHours(1)));
        etsy.Setup(x => x.GetOwnShopIdAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync("12345");

        var result = await service.CompleteEtsyConnectionAsync("code", "s1", null);

        result.Outcome.Should().Be(EtsyConnectionOutcomes.ShopTaken);
        result.RedirectUrl.Should().EndWith("?etsy=shop_taken");
        rows.Should().ContainSingle().Which.UserId.Should().NotBe(userId);
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_StoresEncryptedTokensAndTheReconnectDeadline()
    {
        var service = Create();
        Pending("s1");
        var accessExpiry = clock.GetUtcNow().AddHours(1);
        etsy.Setup(x => x.ExchangeCodeAsync("code", "verifier", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyTokenSet("access", "refresh", accessExpiry));
        etsy.Setup(x => x.GetOwnShopIdAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync("12345");
        etsy.Setup(x => x.GetShopAsync("access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync(Shop);

        var result = await service.CompleteEtsyConnectionAsync("code", "s1", null);

        result.Outcome.Should().Be(EtsyConnectionOutcomes.Connected);
        var row = rows.Should().ContainSingle().Subject;
        row.UserId.Should().Be(userId);
        row.ShopName.Should().Be("WildFigStudio");
        row.EtsyOauthTokenEncrypted.Should().Be($"enc:{accessExpiry.ToUnixTimeSeconds()}:access");
        row.EtsyRefreshTokenEncrypted.Should().Be("enc:refresh");
        // The dashboard reads oauth_expires_at as "reconnect by": the refresh token's end, not the hour-long access token's.
        row.OauthExpiresAt.Should().Be((clock.GetUtcNow() + ShopService.RefreshTokenLifetime).UtcDateTime);
        row.IsActive.Should().BeTrue();
        row.IsDefault.Should().BeTrue();
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_AccountWithoutShop_IsNoShopAndStoresNothing()
    {
        var service = Create();
        Pending("s1");
        etsy.Setup(x => x.ExchangeCodeAsync("code", "verifier", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyTokenSet("access", "refresh", clock.GetUtcNow().AddHours(1)));
        etsy.Setup(x => x.GetOwnShopIdAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        (await service.CompleteEtsyConnectionAsync("code", "s1", null)).Outcome.Should().Be(EtsyConnectionOutcomes.NoShop);
        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompleteEtsyConnectionAsync_EtsyFailure_IsFailedNotAnException()
    {
        var service = Create();
        Pending("s1");
        etsy.Setup(x => x.ExchangeCodeAsync("code", "verifier", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unavailable, "down"));

        (await service.CompleteEtsyConnectionAsync("code", "s1", null)).Outcome.Should().Be(EtsyConnectionOutcomes.Failed);
    }

    [TestMethod]
    public async Task GetMyShopAsync_NoConnection_IsNotConnected()
    {
        var result = await Create().GetMyShopAsync();
        result.Value.Status.Should().Be(ShopConnectionStatuses.NotConnected);
        result.Value.Shop.Should().BeNull();
    }

    [TestMethod]
    public async Task GetMyShopAsync_ValidAccessToken_ReadsTheShopWithoutRefreshing()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddMinutes(30));
        etsy.Setup(x => x.GetShopAsync("old-access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync(Shop);

        var result = await service.GetMyShopAsync();

        result.Value.Status.Should().Be(ShopConnectionStatuses.Connected);
        result.Value.Shop!.ActiveListingCount.Should().Be(42);
        etsy.Verify(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetMyShopAsync_ExpiredAccessToken_RefreshesAndStoresTheRotatedPair()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddMinutes(-5));
        var newExpiry = clock.GetUtcNow().AddHours(1);
        etsy.Setup(x => x.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyTokenSet("new-access", "new-refresh", newExpiry));
        etsy.Setup(x => x.GetShopAsync("new-access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync(Shop);

        var result = await service.GetMyShopAsync();

        result.Value.Status.Should().Be(ShopConnectionStatuses.Connected);
        row.EtsyOauthTokenEncrypted.Should().Be($"enc:{newExpiry.ToUnixTimeSeconds()}:new-access");
        row.EtsyRefreshTokenEncrypted.Should().Be("enc:new-refresh");
        row.OauthExpiresAt.Should().Be((clock.GetUtcNow() + ShopService.RefreshTokenLifetime).UtcDateTime);
    }

    [TestMethod]
    public async Task GetMyShopAsync_RevokedGrant_AsksToReconnectAndMarksTheConnectionExpired()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddMinutes(-5));
        etsy.Setup(x => x.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unauthorized, "invalid_grant"));

        var result = await service.GetMyShopAsync();

        result.Value.Status.Should().Be(ShopConnectionStatuses.ReconnectRequired);
        result.Value.Shop!.ShopName.Should().Be("WildFigStudio");
        row.OauthExpiresAt.Should().Be(clock.GetUtcNow().UtcDateTime);
    }

    [TestMethod]
    public async Task GetMyShopAsync_EtsyDown_KeepsTheConnectionAndShowsTheStoredShop()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddMinutes(30));
        var reconnectBy = row.OauthExpiresAt;
        etsy.Setup(x => x.GetShopAsync("old-access", "12345", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unavailable, "down"));

        var result = await service.GetMyShopAsync();

        result.Value.Status.Should().Be(ShopConnectionStatuses.Unavailable);
        result.Value.Shop!.ShopName.Should().Be("WildFigStudio");
        row.OauthExpiresAt.Should().Be(reconnectBy);
    }

    private void ShopReads()
    {
        etsy.Setup(x => x.GetShopAsync("old-access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync(Shop);
        etsy.Setup(x => x.GetShopSetupAsync("old-access", "12345", It.IsAny<CancellationToken>())).ReturnsAsync(new EtsyShopSetup(1, 1, 0));
        etsy.Setup(x => x.GetListingsAsync("old-access", "12345", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EtsyListingPage(0, []));
        etsy.Setup(x => x.GetReviewsAsync("old-access", "12345", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    [TestMethod]
    public async Task GetShopInsightsAsync_CachedAndNotRefreshing_DoesNotCallEtsy()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddMinutes(30));
        var cached = new APCS.Application.Features.Shops.Dtos.Response.GetShopInsightsResponseDto(
            ShopConnectionStatuses.Connected, clock.GetUtcNow(), "USD", [], null, null, null, []);
        cache.Setup(x => x.GetAsync<APCS.Application.Features.Shops.Dtos.Response.GetShopInsightsResponseDto>(ShopService.InsightsKey(userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        (await service.GetShopInsightsAsync(refresh: false)).Value.Should().BeSameAs(cached);
        etsy.Verify(x => x.GetShopAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetShopInsightsAsync_ARateLimitedSectionOnlyBlanksThatSection()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddMinutes(30));
        ShopReads();
        etsy.Setup(x => x.GetReceiptsAsync("old-access", "12345", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.RateLimited, "slow down"));

        var result = (await service.GetShopInsightsAsync(refresh: true)).Value;

        result.Status.Should().Be(ShopConnectionStatuses.Connected);
        result.Orders.Should().BeNull();
        result.Listings.Should().NotBeNull();
        result.Reviews.Should().NotBeNull();
        result.UnavailableSections.Should().Equal("orders");
    }

    [TestMethod]
    public async Task GetShopInsightsAsync_LostGrant_AsksToReconnect()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddMinutes(30));
        ShopReads();
        etsy.Setup(x => x.GetReceiptsAsync("old-access", "12345", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtsyApiException(EtsyFailureKind.Unauthorized, "revoked"));

        var result = (await service.GetShopInsightsAsync(refresh: true)).Value;

        result.Status.Should().Be(ShopConnectionStatuses.ReconnectRequired);
        row.OauthExpiresAt.Should().Be(clock.GetUtcNow().UtcDateTime);
    }

    [TestMethod]
    public async Task DisconnectEtsyAsync_ForgetsTheTokens()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddMinutes(30));

        var result = await service.DisconnectEtsyAsync();

        result.IsSuccess.Should().BeTrue();
        row.IsActive.Should().BeFalse();
        row.DeletedAt.Should().NotBeNull();
        row.EtsyOauthTokenEncrypted.Should().BeEmpty();
        row.EtsyRefreshTokenEncrypted.Should().BeNull();
    }

    [TestMethod]
    public async Task DisconnectEtsyAsync_NothingConnected_IsNotFound()
    {
        (await Create().DisconnectEtsyAsync()).Error.Type.Should().Be(ErrorType.NotFound);
    }
}
