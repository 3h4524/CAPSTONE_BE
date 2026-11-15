using System.Linq.Expressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Social;
using APCS.Application.Features.Social;
using APCS.Application.Features.Social.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Social;

[TestClass]
public sealed class PinterestServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid productId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly Mock<IPinterestClient> client = new();
    private readonly Mock<IRepository<PinterestIntegration>> integrations = new();
    private readonly Mock<IRepository<SocialMediaShare>> shares = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly List<PinterestIntegration> rows = [];
    private readonly List<SocialMediaShare> shareRows = [];

    private PinterestService Create()
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        client.SetupGet(x => x.IsConfigured).Returns(true);
        client.Setup(x => x.BuildAuthorizationUrl(It.IsAny<string>())).Returns((string state) => $"https://www.pinterest.com/oauth/?state={state}");
        client.Setup(x => x.BuildReturnUrl(It.IsAny<string>())).Returns((string outcome) => $"http://localhost:3000/pinterest/callback?pinterest={outcome}");
        // Tokens are "encrypted" by a visible prefix, so the tests can read what was stored.
        client.Setup(x => x.Protect(It.IsAny<string>())).Returns((string value) => $"enc:{value}");
        client.Setup(x => x.Unprotect(It.IsAny<string>())).Returns((string value) => value["enc:".Length..]);
        integrations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<PinterestIntegration, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<PinterestIntegration, bool>> predicate, CancellationToken _) => rows.Where(predicate.Compile()).ToList());
        integrations.Setup(x => x.AddAsync(It.IsAny<PinterestIntegration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((PinterestIntegration row, bool _, CancellationToken _) => rows.Add(row));
        shares.Setup(x => x.Query()).Returns(() => shareRows.AsQueryable().BuildMock());
        shares.Setup(x => x.AddAsync(It.IsAny<SocialMediaShare>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((SocialMediaShare row, bool _, CancellationToken _) => shareRows.Add(row));
        return new PinterestService(current.Object, cache.Object, client.Object, integrations.Object, shares.Object, unitOfWork.Object, clock,
            NullLogger<PinterestService>.Instance);
    }

    private PinterestIntegration Connected(DateTimeOffset accessExpiresAt)
    {
        var row = new PinterestIntegration
        {
            Id = Guid.NewGuid(), UserId = userId, PinterestUsername = "wildfig",
            AccessTokenEncrypted = "enc:old-access", AccessExpiresAt = accessExpiresAt.UtcDateTime,
            RefreshTokenEncrypted = "enc:old-refresh", RefreshExpiresAt = clock.GetUtcNow().AddDays(300).UtcDateTime,
            CreatedAt = clock.GetUtcNow().AddDays(-10).UtcDateTime, UpdatedAt = clock.GetUtcNow().AddDays(-10).UtcDateTime,
        };
        rows.Add(row);
        return row;
    }

    private PinToPost Pin(Guid mockupId) => new(mockupId, $"https://cdn.test/{mockupId}.jpg", "Cat Tee", "A soft tee.", "https://www.etsy.com/listing/1", "A cat tee");

    [TestMethod]
    public async Task CompleteConnectionAsync_WithAKnownState_StoresTheEncryptedTokensAndTheUsername()
    {
        var service = Create();
        cache.Setup(x => x.GetAsync<PinterestService.PendingPinterestConnection>(PinterestService.StateKey("s1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestService.PendingPinterestConnection(userId));
        client.Setup(x => x.ExchangeCodeAsync("code-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestTokenSet("access", "refresh", clock.GetUtcNow().AddDays(30), clock.GetUtcNow().AddDays(365)));
        client.Setup(x => x.GetUsernameAsync("access", It.IsAny<CancellationToken>())).ReturnsAsync("wildfig");

        var result = await service.CompleteConnectionAsync("code-1", "s1", null);

        result.Outcome.Should().Be(PinterestConnectionOutcomes.Connected);
        rows.Should().ContainSingle();
        rows[0].PinterestUsername.Should().Be("wildfig");
        rows[0].AccessTokenEncrypted.Should().Be("enc:access");
        rows[0].RefreshTokenEncrypted.Should().Be("enc:refresh");
        cache.Verify(x => x.RemoveAsync(PinterestService.StateKey("s1"), It.IsAny<CancellationToken>()), Times.Once, "a state is used once");
    }

    [TestMethod]
    public async Task CompleteConnectionAsync_WithAnUnknownState_ReportsExpired_WithoutCallingPinterest()
    {
        var service = Create();

        var result = await service.CompleteConnectionAsync("code-1", "unknown", null);

        result.Outcome.Should().Be(PinterestConnectionOutcomes.Expired);
        client.Verify(x => x.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PostPinsAsync_SkipsMockupsAlreadyPinnedToTheBoard_AndRecordsTheNewPins()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddDays(20));
        var done = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        shareRows.Add(new SocialMediaShare
        {
            Id = Guid.NewGuid(), ProductId = productId, MockupImageId = done, Platform = "pinterest", DestinationId = "b-1",
            ShareStatus = "posted", ExternalPostId = "old", ExternalPlatformUrl = "https://www.pinterest.com/pin/old/",
        });
        client.Setup(x => x.CreatePinAsync("old-access", It.Is<PinterestNewPin>(pin => pin.BoardId == "b-1" && pin.ImageUrl.Contains(fresh.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestCreatedPin("new", "https://www.pinterest.com/pin/new/"));

        var result = await service.PostPinsAsync(userId, productId, "b-1", [Pin(done), Pin(fresh)]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posted.Select(pin => (pin.PinId, pin.AlreadyPosted)).Should().Equal(("old", true), ("new", false));
        client.Verify(x => x.CreatePinAsync(It.IsAny<string>(), It.IsAny<PinterestNewPin>(), It.IsAny<CancellationToken>()), Times.Once);
        shareRows.Should().Contain(row => row.MockupImageId == fresh && row.ShareStatus == "posted" && row.ExternalPostId == "new"
            && row.DestinationId == "b-1" && row.PromoVideoId == null);
    }

    [TestMethod]
    public async Task PostPinsAsync_RenewsAnAccessTokenAboutToExpire_BeforePosting()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddHours(2));
        client.Setup(x => x.RefreshAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestTokenSet("new-access", null, clock.GetUtcNow().AddDays(30), null));
        client.Setup(x => x.CreatePinAsync("new-access", It.IsAny<PinterestNewPin>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestCreatedPin("p", "https://www.pinterest.com/pin/p/"));

        var result = await service.PostPinsAsync(userId, productId, "b-1", [Pin(Guid.NewGuid())]);

        result.IsSuccess.Should().BeTrue();
        row.AccessTokenEncrypted.Should().Be("enc:new-access");
        row.RefreshTokenEncrypted.Should().Be("enc:old-refresh", "Pinterest kept the refresh token");
        integrations.Verify(x => x.UpdateAsync(row, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PostPinsAsync_WhenPinterestRefusesTheGrant_AsksToConnectAgain()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddDays(20));
        client.Setup(x => x.CreatePinAsync(It.IsAny<string>(), It.IsAny<PinterestNewPin>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PinterestApiException(PinterestFailureKind.Unauthorized, "Pinterest returned 401."));

        var result = await service.PostPinsAsync(userId, productId, "b-1", [Pin(Guid.NewGuid()), Pin(Guid.NewGuid())]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("social.pinterest_reconnect_required");
        client.Verify(x => x.CreatePinAsync(It.IsAny<string>(), It.IsAny<PinterestNewPin>(), It.IsAny<CancellationToken>()), Times.Once, "later pins would fail the same way");
    }

    [TestMethod]
    public async Task PostPinsAsync_ARefusedPin_IsReportedWhileTheOthersArePosted()
    {
        var service = Create();
        Connected(clock.GetUtcNow().AddDays(20));
        var bad = Guid.NewGuid();
        client.Setup(x => x.CreatePinAsync(It.IsAny<string>(), It.IsAny<PinterestNewPin>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PinterestCreatedPin("ok", "https://www.pinterest.com/pin/ok/"));
        client.Setup(x => x.CreatePinAsync(It.IsAny<string>(), It.Is<PinterestNewPin>(pin => pin.ImageUrl.Contains(bad.ToString())), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PinterestApiException(PinterestFailureKind.Rejected, "Pinterest returned 400.", "Invalid image"));

        var result = await service.PostPinsAsync(userId, productId, "b-1", [Pin(bad), Pin(Guid.NewGuid())]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Posted.Should().ContainSingle();
        result.Value.Failures.Should().ContainSingle().Which.Should().Contain("Invalid image");
        shareRows.Should().Contain(row => row.MockupImageId == bad && row.ShareStatus == "failed");
    }

    [TestMethod]
    public async Task GetConnectionAsync_WithoutAnAccount_IsNotConnected()
    {
        var service = Create();

        var result = await service.GetConnectionAsync();

        result.Value.Status.Should().Be(PinterestConnectionStatuses.NotConnected);
        result.Value.Configured.Should().BeTrue();
    }

    [TestMethod]
    public async Task DisconnectAsync_BlanksTheStoredTokens()
    {
        var service = Create();
        var row = Connected(clock.GetUtcNow().AddDays(20));

        var result = await service.DisconnectAsync();

        result.IsSuccess.Should().BeTrue();
        row.DeletedAt.Should().NotBeNull();
        row.AccessTokenEncrypted.Should().BeEmpty();
        row.RefreshTokenEncrypted.Should().BeNull();
    }
}
