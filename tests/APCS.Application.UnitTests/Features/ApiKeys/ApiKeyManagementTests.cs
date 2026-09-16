using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.ApiKeys;
using APCS.Application.Features.ApiKeys.Dtos.Request;
using APCS.Application.Features.ApiKeys.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.ApiKeys;

[TestClass]
public sealed class ApiKeyManagementTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Mock<ICurrentUser> current = new();
    private readonly Mock<IAccountService> accounts = new();
    private readonly Mock<IApiKeyRepository> repository = new();
    private readonly Mock<IApiKeyCredentials> credentials = new();
    private readonly Mock<IUnitOfWorkTransaction> transaction = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private ApiKeyService Create(params ApiKey[] rows)
    {
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(owner);
        accounts.Setup(x => x.FindByIdAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfoDto(owner, "seller@example.com", "Seller", true, true));
        accounts.Setup(x => x.GetRolesAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "Seller" });
        repository.Setup(x => x.LockOwnerAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        repository.Setup(x => x.ListOwnedAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        credentials.Setup(x => x.ValidateAsync("openai", "test-key-1234", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        credentials.Setup(x => x.Protect("test-key-1234")).Returns("encrypted-value");
        return new ApiKeyService(current.Object, accounts.Object, clock,
            repository.Object, credentials.Object, new SaveApiKeyValidator());
    }
    private static SaveApiKeyRequestDto Request(string? key = "test-key-1234", bool confirmed = true) =>
        new() { Provider = "openai", Name = " Workspace ", Environment = "Production", ApiKey = key, Confirmed = confirmed };
    private ApiKey Row() => new() { Id = Guid.NewGuid(), UserId = owner, ServiceProvider = "openai", AuthType = "api_key",
        KeyIdentifier = "Original", KeyValueEncrypted = "old-encrypted", KeyLast4 = "old4", IsActive = true,
        LastCheckSucceeded = true, LastCheckedAt = clock.GetUtcNow().UtcDateTime };

    [TestMethod]
    public async Task Add_EncryptsAndCommitsOnlyValidatedKey()
    {
        var service = Create();
        (await service.SaveAsync(null, Request())).IsSuccess.Should().BeTrue();
        repository.Verify(x => x.AddAsync(It.Is<ApiKey>(key => key.UserId == owner && key.KeyValueEncrypted == "encrypted-value" &&
            key.KeyLast4 == "1234" && key.KeyIdentifier == "Workspace" && key.IsActive == true && key.LastCheckSucceeded == true &&
            key.LastUsedAt == clock.GetUtcNow().UtcDateTime), true, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Add_MissingConfirmationOrKey_DoesNotAccessCredentialsOrWrite()
    {
        var service = Create();
        (await service.SaveAsync(null, Request(confirmed: false))).Error.Type.Should().Be(ErrorType.Validation);
        (await service.SaveAsync(null, Request(null))).Error.Code.Should().Be("MSG01");
        credentials.VerifyNoOtherCalls();
        repository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Add_DuplicateConnectedProvider_IsBlockedBeforeExternalRequest()
    {
        var row = Row(); row.ServiceProvider = "OpenAI";
        var service = Create(row);
        (await service.SaveAsync(null, Request())).Error.Code.Should().Be("MSG51");
        credentials.VerifyNoOtherCalls();
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Edit_InvalidReplacement_PreservesExistingCredentialAndMetadata()
    {
        var row = Row(); var service = Create(row);
        credentials.Setup(x => x.ValidateAsync("openai", "test-key-1234", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await service.SaveAsync(row.Id, Request())).Error.Code.Should().Be("MSG50");
        row.KeyValueEncrypted.Should().Be("old-encrypted"); row.KeyIdentifier.Should().Be("Original");
        repository.Verify(x => x.UpdateAsync(It.IsAny<ApiKey>(), true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Edit_BlankReplacement_UpdatesMetadataWithoutDecrypting()
    {
        var row = Row(); var service = Create(row);
        (await service.SaveAsync(row.Id, Request(null))).IsSuccess.Should().BeTrue();
        row.KeyValueEncrypted.Should().Be("old-encrypted"); row.KeyIdentifier.Should().Be("Workspace");
        credentials.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Delete_SoftDeletesAndRemovesCredential()
    {
        var row = Row(); var service = Create(row);
        (await service.DeleteAsync(row.Id)).IsSuccess.Should().BeTrue();
        row.DeletedAt.Should().Be(clock.GetUtcNow().UtcDateTime); row.IsActive.Should().BeFalse();
        row.KeyValueEncrypted.Should().BeEmpty(); row.KeyLast4.Should().BeNull();
        repository.Verify(x => x.UpdateAsync(row, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Mutations_ForeignOrDeletedIds_ReturnNotFound()
    {
        var service = Create(); var foreign = Guid.NewGuid();
        (await service.SaveAsync(foreign, Request())).Error.Type.Should().Be(ErrorType.NotFound);
        (await service.DeleteAsync(foreign)).Error.Type.Should().Be(ErrorType.NotFound);
        (await service.ValidateAsync(foreign)).Error.Type.Should().Be(ErrorType.NotFound);
        credentials.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Mutations_InactiveSeller_DoesNotAccessRepository()
    {
        var service = Create();
        accounts.Setup(x => x.FindByIdAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountInfoDto(owner, "seller@example.com", "Seller", false, true));
        (await service.SaveAsync(null, Request())).Error.Type.Should().Be(ErrorType.Forbidden);
        (await service.DeleteAsync(Guid.NewGuid())).Error.Type.Should().Be(ErrorType.Forbidden);
        (await service.ValidateAsync(Guid.NewGuid())).Error.Type.Should().Be(ErrorType.Forbidden);
        repository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Validate_FailedCheck_PersistsAttentionStateWithoutChangingSecret()
    {
        var row = Row(); var service = Create(row);
        credentials.Setup(x => x.Unprotect("old-encrypted")).Returns("invalid-key");
        (await service.ValidateAsync(row.Id)).Error.Code.Should().Be("MSG50");
        row.LastCheckSucceeded.Should().BeFalse(); row.IsActive.Should().BeFalse();
        row.KeyValueEncrypted.Should().Be("old-encrypted");
        transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
