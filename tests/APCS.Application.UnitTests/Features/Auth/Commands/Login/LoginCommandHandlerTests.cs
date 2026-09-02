using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Commands.Login;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.RefreshToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Login;

[TestClass]
public sealed class LoginCommandHandlerTests
{
    [TestMethod]
    public async Task Handle_WhenUserDoesNotExist_ReturnsInvalidCredentials()
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByEmailAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);

        var result = await CreateHandler(identity).Handle(
            CreateCommand(),
            CancellationToken.None);

        AssertFailure(result.Error.Code, ErrorCodes.InvalidCredentials);
        identity.Verify(
            service => service.ValidateCredentialsAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenUserIsInactive_ReturnsInactiveFailure()
    {
        var identity = CreateIdentityWithUser(AuthTestData.ActiveUser with { IsActive = false });

        var result = await CreateHandler(identity).Handle(CreateCommand(), CancellationToken.None);

        AssertFailure(result.Error.Code, ErrorCodes.UserInactive);
    }

    [TestMethod]
    public async Task Handle_WhenUserIsAlreadyLocked_ReturnsLockedFailure()
    {
        var identity = CreateIdentityWithUser(AuthTestData.ActiveUser with { IsLockedOut = true });

        var result = await CreateHandler(identity).Handle(CreateCommand(), CancellationToken.None);

        AssertFailure(result.Error.Code, ErrorCodes.UserLockedOut);
    }

    [TestMethod]
    [DataRow(false, true, false, "auth.user_locked_out")]
    [DataRow(false, false, true, "auth.invalid_credentials")]
    [DataRow(false, false, false, "auth.invalid_credentials")]
    public async Task Handle_WhenCredentialsAreRejected_ReturnsExpectedFailure(
        bool succeeded,
        bool isLockedOut,
        bool isNotAllowed,
        string expectedCode)
    {
        var identity = CreateIdentityWithUser(AuthTestData.ActiveUser);
        identity.Setup(service => service.ValidateCredentialsAsync(
                AuthTestData.ActiveUser.Id,
                "Password1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialValidationResult(succeeded, isLockedOut, isNotAllowed));

        var result = await CreateHandler(identity).Handle(CreateCommand(), CancellationToken.None);

        AssertFailure(result.Error.Code, expectedCode);
    }

    [TestMethod]
    public async Task Handle_WithValidCredentials_PersistsSessionAndTouchesLastLogin()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var identity = CreateIdentityWithUser(AuthTestData.ActiveUser, cancellationToken);
        identity.Setup(service => service.ValidateCredentialsAsync(
                AuthTestData.ActiveUser.Id,
                "Password1",
                cancellationToken))
            .ReturnsAsync(new CredentialValidationResult(true, false, false));
        identity.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        identity.Setup(service => service.TouchLastLoginAsync(
                AuthTestData.ActiveUser.Id,
                AuthTestData.UtcNow,
                cancellationToken))
            .Returns(Task.CompletedTask);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        RefreshTokenEntity? savedToken = null;
        var repository = new Mock<IRefreshTokenRepository>();
        repository.Setup(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()))
            .Callback<RefreshTokenEntity>(token => savedToken = token);
        var handler = CreateHandler(identity, dbContext, repository, AuthTestData.CreateJwtService());

        var result = await handler.Handle(
            new LoginCommand(" Seller@Example.com ", "Password1"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        savedToken!.TokenHash.Should().Be("new-refresh-token-hash");
        identity.Verify(service => service.TouchLastLoginAsync(
            AuthTestData.ActiveUser.Id,
            AuthTestData.UtcNow,
            cancellationToken), Times.Once);
        dbContext.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WhenCredentialsFail_DoesNotPersistOrTouchLogin()
    {
        var identity = CreateIdentityWithUser(AuthTestData.ActiveUser);
        identity.Setup(service => service.ValidateCredentialsAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialValidationResult(false, false, false));
        var dbContext = new Mock<IUnitOfWork>();
        var repository = new Mock<IRefreshTokenRepository>();

        await CreateHandler(identity, dbContext, repository).Handle(CreateCommand(), CancellationToken.None);

        repository.Verify(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()), Times.Never);
        identity.Verify(service => service.TouchLastLoginAsync(
            It.IsAny<int>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static LoginCommand CreateCommand() =>
        new("seller@example.com", "Password1");

    private static Mock<IIdentityService> CreateIdentityWithUser(
        IdentityUserInfo user,
        CancellationToken cancellationToken = default)
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByEmailAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(user);
        return identity;
    }

    private static LoginCommandHandler CreateHandler(
        Mock<IIdentityService> identity,
        Mock<IUnitOfWork>? dbContext = null,
        Mock<IRefreshTokenRepository>? repository = null,
        Mock<IJwtService>? jwtService = null) => new(
        identity.Object,
        (dbContext ?? new Mock<IUnitOfWork>()).Object,
        (repository ?? new Mock<IRefreshTokenRepository>()).Object,
        (jwtService ?? new Mock<IJwtService>()).Object,
        AuthTestData.CreateTimeProvider());

    private static void AssertFailure(string actualCode, string expectedCode)
    {
        actualCode.Should().Be(expectedCode);
    }
}
