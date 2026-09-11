using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class LoginTests
{
    [TestMethod]
    public async Task LoginAsync_WhenUserDoesNotExist_ReturnsInvalidCredentials()
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByEmailAsync("seller@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);

        var result = await AuthTestData.CreateService(account).LoginAsync(CreateRequest(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.InvalidCredentials);
        account.Verify(service => service.ValidateCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LoginAsync_WhenUserIsInactive_ReturnsInactiveFailureWithoutCheckingPassword()
    {
        var account = CreateAccountWithUser(AuthTestData.ActiveUser with { IsActive = false });

        var result = await AuthTestData.CreateService(account).LoginAsync(CreateRequest(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserInactive);
        account.Verify(service => service.ValidateCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LoginAsync_WhenEmailIsNotVerified_ReturnsEmailNotVerifiedWithoutCheckingPassword()
    {
        var account = CreateAccountWithUser(AuthTestData.UnverifiedUser);

        var result = await AuthTestData.CreateService(account).LoginAsync(CreateRequest(), CancellationToken.None);

        // An account pending verification is also inactive; the caller needs the answer that
        // tells it a verification link can be resent.
        result.Error.Code.Should().Be(ErrorCodes.EmailNotVerified);
        account.Verify(service => service.ValidateCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LoginAsync_WhenCredentialsAreRejected_DoesNotPersistOrTouchLogin()
    {
        var account = CreateAccountWithUser(AuthTestData.ActiveUser);
        account.Setup(service => service.ValidateCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var unitOfWork = new Mock<IUnitOfWork>();
        var repository = new Mock<IAuthTokenRepository>();

        var result = await AuthTestData.CreateService(account, unitOfWork, repository)
            .LoginAsync(CreateRequest(), CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.InvalidCredentials);
        repository.Verify(
            candidate => candidate.AddAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
        account.Verify(service => service.TouchLastLoginAsync(
            It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LoginAsync_WithValidCredentials_PersistsSessionAndTouchesLastLogin()
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
        repository.Setup(candidate => candidate.AddAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, bool, CancellationToken>((token, _, _) => savedToken = token)
            .Returns(Task.CompletedTask);
        var jwtService = AuthTestData.CreateJwtService();

        var result = await AuthTestData.CreateService(account, unitOfWork, repository, jwtService)
            .LoginAsync(new LoginRequestDto(" Seller@Example.com ", "Password1"), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        result.Value.User!.Roles.Should().Equal(AuthTestData.Roles);
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

    private static LoginRequestDto CreateRequest() => new("seller@example.com", "Password1");

    private static Mock<IAccountService> CreateAccountWithUser(
        AccountInfoDto user,
        CancellationToken cancellationToken = default)
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByEmailAsync("seller@example.com", cancellationToken))
            .ReturnsAsync(user);
        return account;
    }
}
