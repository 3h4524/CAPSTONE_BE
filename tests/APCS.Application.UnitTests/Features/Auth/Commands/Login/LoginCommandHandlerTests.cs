using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Commands.Login;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Login;

[TestClass]
public sealed class LoginCommandHandlerTests
{
    [TestMethod]
    public async Task Handle_WhenUserDoesNotExist_ReturnsInvalidCredentials()
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByEmailAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfo?)null);

        var result = await CreateHandler(account).Handle(CreateCommand(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.InvalidCredentials);
        account.Verify(service => service.ValidateCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenUserIsInactive_ReturnsInactiveFailureWithoutCheckingPassword()
    {
        var account = CreateAccountWithUser(AuthTestData.ActiveUser with { IsActive = false });

        var result = await CreateHandler(account).Handle(CreateCommand(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserInactive);
        account.Verify(service => service.ValidateCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenCredentialsAreRejected_DoesNotPersistOrTouchLogin()
    {
        var account = CreateAccountWithUser(AuthTestData.ActiveUser);
        account.Setup(service => service.ValidateCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();

        var result = await CreateHandler(account, unitOfWork, repository)
            .Handle(CreateCommand(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.InvalidCredentials);
        repository.Verify(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()), Times.Never);
        account.Verify(service => service.TouchLastLoginAsync(
            It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WithValidCredentials_PersistsSessionAndTouchesLastLogin()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var account = CreateAccountWithUser(AuthTestData.ActiveUser, cancellationToken);
        account.Setup(service => service.ValidateCredentialsAsync(
                AuthTestData.ActiveUser.Id,
                "Password1",
                cancellationToken))
            .ReturnsAsync(true);
        account.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);
        RefreshTokenEntity? savedToken = null;
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()))
            .Callback<RefreshTokenEntity>(token => savedToken = token);
        var jwtService = AuthTestData.CreateJwtService();

        var result = await CreateHandler(account, unitOfWork, repository, jwtService)
            .Handle(new LoginCommand(" Seller@Example.com ", "Password1"), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        result.Value.User.Roles.Should().Equal(AuthTestData.Roles);
        savedToken.Should().NotBeNull();
        savedToken!.TokenHash.Should().Be("new-refresh-token-hash");
        account.Verify(service => service.FindByEmailAsync("seller@example.com", cancellationToken), Times.Once);
        account.Verify(service => service.TouchLastLoginAsync(
            AuthTestData.ActiveUser.Id, AuthTestData.UtcNow, cancellationToken), Times.Once);
        jwtService.Verify(service => service.GenerateAccessToken(
            AuthTestData.ActiveUser.Id,
            AuthTestData.ActiveUser.Email,
            AuthTestData.Roles), Times.Once);
        unitOfWork.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    private static LoginCommand CreateCommand() => new("seller@example.com", "Password1");

    private static Mock<IAccountService> CreateAccountWithUser(
        AccountInfo user,
        CancellationToken cancellationToken = default)
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByEmailAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(user);
        return account;
    }

    private static LoginCommandHandler CreateHandler(
        Mock<IAccountService> account,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IAuthTokenRepository>? repository = null,
        Mock<IJwtService>? jwtService = null) => new(
        account.Object,
        (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
        (repository ?? new Mock<IAuthTokenRepository>()).Object,
        (jwtService ?? new Mock<IJwtService>()).Object,
        AuthTestData.CreateTimeProvider());
}
