using System.Data.Common;
using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Application.Features.ApiKeys;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.ApiKeys;

[TestClass]
public sealed class ApiKeyServiceTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<IAccountService> accounts = new();
    private readonly Mock<IApiKeyRepository> repository = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

    private ApiKeyService Create(params ApiKeyMetadata[] rows)
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        accounts.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfoDto(userId, "seller@example.com", "Seller", true, true, "active", null));
        accounts.Setup(x => x.GetRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Seller" });
        repository.Setup(x => x.ListMetadataOwnedAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        return new ApiKeyService(current.Object, accounts.Object, clock,
            repository.Object, Mock.Of<IApiKeyCredentials>(),
            new APCS.Application.Features.ApiKeys.Validators.SaveApiKeyValidator());
    }

    private ApiKeyMetadata Row(string provider = "openai") => new(
        Guid.NewGuid(), provider, "Workspace", "7F2A", "api_key", null, "Production",
        true, null, clock.GetUtcNow().AddMinutes(-4).UtcDateTime,
        clock.GetUtcNow().AddMinutes(-1).UtcDateTime, true);

    [TestMethod]
    public async Task ListMineAsync_Anonymous_DoesNotReadAccountsOrKeys()
    {
        var service = Create();
        current.SetupGet(x => x.IsAuthenticated).Returns(false);
        var result = await service.ListMineAsync();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        accounts.Verify(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(x => x.ListMetadataOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListMineAsync_MissingUserId_IsUnauthorized()
    {
        var service = Create();
        current.SetupGet(x => x.UserId).Returns((Guid?)null);
        (await service.ListMineAsync()).Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [TestMethod]
    public async Task ListMineAsync_InactiveAccount_DoesNotReadKeys()
    {
        var service = Create();
        accounts.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfoDto(userId, "seller@example.com", "Seller", false, true, "suspended", null));
        (await service.ListMineAsync()).Error.Type.Should().Be(ErrorType.Forbidden);
        repository.Verify(x => x.ListMetadataOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListMineAsync_DeletedOrMissingAccount_DoesNotReadKeys()
    {
        var service = Create();
        accounts.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((AccountInfoDto?)null);
        (await service.ListMineAsync()).Error.Type.Should().Be(ErrorType.Forbidden);
        repository.Verify(x => x.ListMetadataOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListMineAsync_RevokedSellerRole_DoesNotReadKeys()
    {
        var service = Create();
        accounts.Setup(x => x.GetRolesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "Admin" });
        (await service.ListMineAsync()).Error.Type.Should().Be(ErrorType.Forbidden);
        repository.Verify(x => x.ListMetadataOwnedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ListMineAsync_Empty_IsNotReadyAndHasNoFabricatedCheckTime()
    {
        var result = await Create().ListMineAsync();
        result.Value.Items.Should().BeEmpty();
        result.Value.ConnectedCount.Should().Be(0);
        result.Value.NeedsAttentionCount.Should().Be(0);
        result.Value.IsReady.Should().BeFalse();
        result.Value.LastCheckedAtUtc.Should().BeNull();
    }

    [TestMethod]
    public async Task ListMineAsync_CoreProvidersConnected_IsReadyAndMasksKeys()
    {
        var rows = new[] { Row(), Row("replicate"), Row("printify"), Row("etsy") with { AuthType = "oauth", ConnectedAccountName = "Wild Fig Studio (Etsy)" } };
        var result = await Create(rows).ListMineAsync();
        result.Value.IsReady.Should().BeTrue();
        result.Value.ConnectedCount.Should().Be(4);
        result.Value.NeedsAttentionCount.Should().Be(0);
        result.Value.Items[0].Credential.Should().Be("****7F2A");
        result.Value.Items[3].Credential.Should().Be("Wild Fig Studio (Etsy)");
        result.Value.Items[3].AuthType.Should().Be("OAuth");
        result.Value.LastCheckedAtUtc.Should().Be(clock.GetUtcNow().AddMinutes(-1));
        JsonSerializer.Serialize(result.Value).Should().NotContain("KeyValueEncrypted").And.NotContain("Last4");
    }

    [TestMethod]
    public async Task ListMineAsync_DuplicateProviders_DoNotMakeWorkflowReady()
    {
        var result = await Create(Row(), Row(), Row(), Row("unknown")).ListMineAsync();
        result.Value.IsReady.Should().BeFalse();
        result.Value.Items.Last().ProviderCategory.Should().Be("Other service");
    }

    [TestMethod]
    public async Task ListMineAsync_ExpiredFailedInactiveAndUnknown_AreNotConnected()
    {
        var result = await Create(
            Row() with { ExpiresAt = clock.GetUtcNow().UtcDateTime },
            Row() with { LastCheckSucceeded = false },
            Row() with { IsActive = false },
            Row() with { LastCheckSucceeded = null, LastCheckedAt = null },
            Row() with { LastCheckedAt = null },
            Row() with { IsActive = null },
            Row() with { ExpiresAt = clock.GetUtcNow().AddSeconds(1).UtcDateTime }
        ).ListMineAsync();
        result.Value.Items.Select(x => x.Status).Should().Equal(
            "Reconnect", "Reconnect", "Reconnect", "Needs attention", "Needs attention", "Needs attention", "Connected");
        result.Value.NeedsAttentionCount.Should().Be(6);
        result.Value.ConnectedCount.Should().Be(1);
    }

    [TestMethod]
    public async Task ListMineAsync_InvalidSuffixAndMissingAccountName_AreSafelyRepresented()
    {
        var result = await Create(
            Row() with { Last4 = null, LastUsedAt = null },
            Row() with { Last4 = "full-secret-value" },
            Row() with { Last4 = "abc" },
            Row() with { AuthType = "oauth", ConnectedAccountName = null }
        ).ListMineAsync();
        result.Value.Items.Select(x => x.Credential).Should().Equal("****", "****", "****", "Connected account");
        result.Value.Items[0].LastUsedAtUtc.Should().BeNull();
    }

    [TestMethod]
    public async Task ListMineAsync_MostRecentCheck_UsesMaximumNotLastUsedTime()
    {
        var result = await Create(
            Row() with { LastCheckedAt = null },
            Row() with { LastCheckedAt = clock.GetUtcNow().AddDays(-1).UtcDateTime },
            Row()
        ).ListMineAsync();
        result.Value.LastCheckedAtUtc.Should().Be(clock.GetUtcNow().AddMinutes(-1));
    }

    [TestMethod]
    public async Task ListMineAsync_OwnershipAndCancellation_ArePassedToEveryDependency()
    {
        using var cancellation = new CancellationTokenSource();
        var service = Create(Row());
        await service.ListMineAsync(cancellation.Token);
        accounts.Verify(x => x.FindByIdAsync(userId, cancellation.Token), Times.Once);
        accounts.Verify(x => x.GetRolesAsync(userId, cancellation.Token), Times.Once);
        repository.Verify(x => x.ListMetadataOwnedAsync(userId, cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task ListMineAsync_DatabaseError_ReturnsMsg16WithoutInternalDetails()
    {
        var service = Create();
        repository.Setup(x => x.ListMetadataOwnedAsync(userId, It.IsAny<CancellationToken>())).ThrowsAsync(new TestDatabaseException());
        var result = await service.ListMineAsync();
        result.Error.Code.Should().Be("MSG16");
        result.Error.Message.Should().NotContain("secret");
    }

    [TestMethod]
    public async Task ListMineAsync_Timeout_ReturnsMsg16()
    {
        var service = Create();
        accounts.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        (await service.ListMineAsync()).Error.Code.Should().Be("MSG16");
    }

    [TestMethod]
    public async Task ListMineAsync_Cancellation_IsNotConvertedToDatabaseFailure()
    {
        var service = Create();
        repository.Setup(x => x.ListMetadataOwnedAsync(userId, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        Func<Task> action = async () => await service.ListMineAsync();
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task ListMineAsync_OptionalEtsyNeedsReconnect_CoreWorkflowRemainsReady()
    {
        var result = await Create(Row(), Row("replicate"), Row("printify"),
            Row("etsy") with { LastCheckSucceeded = false }).ListMineAsync();
        result.Value.IsReady.Should().BeTrue();
        result.Value.ConnectedCount.Should().Be(3);
        result.Value.NeedsAttentionCount.Should().Be(1);
    }
    private sealed class TestDatabaseException() : DbException("secret internal connection details");
}
