using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class RefreshTokenTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task RefreshTokenAsync_WhenTokenIsMissing_ReturnsMissingFailure(string? refreshToken)
    {
        var repository = new Mock<IAuthTokenRepository>();
        var jwtService = new Mock<IJwtService>();

        var result = await CreateService(repository, jwtService: jwtService)
            .RefreshTokenAsync(refreshToken, null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenMissing);
        jwtService.Verify(service => service.HashRefreshToken(It.IsAny<string>()), Times.Never);
        repository.Verify(service => service.GetByHashAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenTokenDoesNotExist_ReturnsInvalidFailure()
    {
        var (repository, jwtService) = CreateLookup(null);

        var result = await CreateService(repository, jwtService: jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenInvalid);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenTokenWasRevoked_ReturnsReusedWithoutSavingOrLookingUpAccount()
    {
        var token = AuthTestData.CreateRefreshToken();
        token.Revoke(AuthTestData.UtcNow.AddMinutes(-1));
        var (repository, jwtService) = CreateLookup(token);
        var unitOfWork = new Mock<IUnitOfWork>();
        var account = new Mock<IAccountService>();

        var result = await CreateService(repository, unitOfWork, account, jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        account.Verify(service => service.FindByIdAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenTokenExpired_RevokesAndReturnsExpiredFailure()
    {
        var token = AuthTestData.CreateRefreshToken(AuthTestData.UtcNow);
        var (repository, jwtService) = CreateLookup(token);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateService(repository, unitOfWork, jwtService: jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenExpired);
        token.RevokedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenExpiredTokenSaveConflicts_ReturnsReusedFailure()
    {
        var token = AuthTestData.CreateRefreshToken(AuthTestData.UtcNow);
        var (repository, jwtService) = CreateLookup(token);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateService(repository, unitOfWork, jwtService: jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenOwningAccountDoesNotExist_ReturnsInvalidFailure()
    {
        var (repository, jwtService) = CreateLookup(AuthTestData.CreateRefreshToken());
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountInfoDto?)null);

        var result = await CreateService(repository, account: account, jwtService: jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenInvalid);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenOwningAccountIsInactive_ReturnsInactiveFailure()
    {
        var (repository, jwtService) = CreateLookup(AuthTestData.CreateRefreshToken());
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with { IsActive = false });

        var result = await CreateService(repository, account: account, jwtService: jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.UserInactive);
        account.Verify(service => service.GetRolesAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WithActiveToken_RotatesSessionAndReturnsNewTokens()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var existingToken = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(existingToken, cancellationToken);
        RefreshTokenEntity? addedToken = null;
        repository.Setup(candidate => candidate.AddAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshTokenEntity, bool, CancellationToken>((token, _, _) => addedToken = token)
            .Returns(Task.CompletedTask);
        var account = CreateActiveAccount(cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(2);
        SetupNewSession(jwtService);

        var result = await CreateService(repository, unitOfWork, account, jwtService)
            .RefreshTokenAsync("presented-raw-token", null, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        existingToken.RevokedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        addedToken.Should().NotBeNull();
        addedToken!.TokenHash.Should().Be("new-refresh-token-hash");
        unitOfWork.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task RefreshTokenAsync_WhenRotationSaveConflicts_ReturnsReusedFailure()
    {
        var existingToken = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(existingToken);
        var account = CreateActiveAccount();
        SetupNewSession(jwtService);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateService(repository, unitOfWork, account, jwtService)
            .RefreshTokenAsync("presented-raw-token", null, CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
    }

    private static (Mock<IAuthTokenRepository> Repository, Mock<IJwtService> JwtService) CreateLookup(
        RefreshTokenEntity? token,
        CancellationToken cancellationToken = default)
    {
        var repository = new Mock<IAuthTokenRepository>();
        repository.Setup(candidate => candidate.GetByHashAsync("presented-token-hash", cancellationToken))
            .ReturnsAsync(token);
        var jwtService = new Mock<IJwtService>();
        jwtService.Setup(service => service.HashRefreshToken("presented-raw-token"))
            .Returns("presented-token-hash");
        return (repository, jwtService);
    }

    private static Mock<IAccountService> CreateActiveAccount(CancellationToken cancellationToken = default)
    {
        var account = new Mock<IAccountService>();
        account.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        account.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        return account;
    }

    private static void SetupNewSession(Mock<IJwtService> jwtService)
    {
        jwtService.Setup(service => service.GenerateAccessToken(
                AuthTestData.ActiveUser.Id,
                AuthTestData.ActiveUser.Email,
                AuthTestData.Roles))
            .Returns(new JwtTokenResultDto("access-token", "new-jwt-id", AuthTestData.UtcNow.AddMinutes(15)));
        jwtService.Setup(service => service.GenerateRefreshToken()).Returns("new-raw-refresh-token");
        jwtService.Setup(service => service.HashRefreshToken("new-raw-refresh-token"))
            .Returns("new-refresh-token-hash");
        jwtService.Setup(service => service.GetRefreshTokenExpiresAt(AuthTestData.UtcNow))
            .Returns(AuthTestData.UtcNow.AddDays(7));
    }

    private static AuthService CreateService(
        Mock<IAuthTokenRepository> repository,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IAccountService>? account = null,
        Mock<IJwtService>? jwtService = null) =>
        AuthTestData.CreateService(account, unitOfWork, repository, jwtService);
}
