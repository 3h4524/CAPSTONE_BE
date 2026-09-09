using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth;
using APCS.Application.UnitTests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.AuthServiceTests;

[TestClass]
public sealed class LogoutTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task LogoutAsync_WhenTokenIsMissing_ReturnsSuccessWithoutLookup(string? token)
    {
        var repository = new Mock<IAuthTokenRepository>();
        var service = AuthTestData.CreateService(authTokenRepository: repository);

        var result = await service.LogoutAsync(token, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(candidate => candidate.GetByHashAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LogoutAsync_WhenTokenDoesNotExist_ReturnsSuccessWithoutSaving()
    {
        var (repository, jwtService) = CreateLookup(null);
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await CreateService(repository, unitOfWork, jwtService)
            .LogoutAsync("presented-raw-token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LogoutAsync_WhenTokenAlreadyRevoked_ReturnsSuccessWithoutSaving()
    {
        var token = AuthTestData.CreateRefreshToken();
        token.Revoke(AuthTestData.UtcNow.AddMinutes(-1));
        var (repository, jwtService) = CreateLookup(token);
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await CreateService(repository, unitOfWork, jwtService)
            .LogoutAsync("presented-raw-token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LogoutAsync_WhenTokenIsActive_RevokesAndSaves()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var token = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(token, cancellationToken);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);

        var result = await CreateService(repository, unitOfWork, jwtService)
            .LogoutAsync("presented-raw-token", null, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        token.IsRevoked.Should().BeTrue();
        token.RevokedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        unitOfWork.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task LogoutAsync_WhenConcurrentRevocationWins_ReturnsSuccess()
    {
        var token = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(token);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateService(repository, unitOfWork, jwtService)
            .LogoutAsync("presented-raw-token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.IsRevoked.Should().BeTrue();
        unitOfWork.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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

    private static AuthService CreateService(
        Mock<IAuthTokenRepository> repository,
        Mock<IUnitOfWork> unitOfWork,
        Mock<IJwtService> jwtService) =>
        AuthTestData.CreateService(unitOfWork: unitOfWork, authTokenRepository: repository, jwtService: jwtService);
}
