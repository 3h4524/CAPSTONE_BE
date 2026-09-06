using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Commands.Logout;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.AuthToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.Logout;

[TestClass]
public sealed class LogoutCommandHandlerTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task Handle_WhenTokenIsMissing_ReturnsSuccessWithoutLookup(string? token)
    {
        var repository = new Mock<IAuthTokenRepository>();
        var handler = CreateHandler(repository);

        var result = await handler.Handle(new LogoutCommand(token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Verify(candidate => candidate.GetByHashAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenTokenDoesNotExist_ReturnsSuccessWithoutSaving()
    {
        var (repository, jwtService) = CreateLookup(null);
        var dbContext = new Mock<IUnitOfWork>();

        var result = await CreateHandler(repository, dbContext, jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenTokenAlreadyRevoked_ReturnsSuccessWithoutSaving()
    {
        var token = AuthTestData.CreateRefreshToken();
        token.Revoke(AuthTestData.UtcNow.AddMinutes(-1));
        var (repository, jwtService) = CreateLookup(token);
        var dbContext = new Mock<IUnitOfWork>();

        var result = await CreateHandler(repository, dbContext, jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenTokenIsActive_RevokesAndSaves()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var token = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(token, cancellationToken);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(1);

        var result = await CreateHandler(repository, dbContext, jwtService).Handle(
            CreateCommand(),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        token.IsRevoked.Should().BeTrue();
        token.RevokedAt.Should().Be(AuthTestData.UtcNow.UtcDateTime);
        dbContext.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WhenConcurrentRevocationWins_ReturnsSuccess()
    {
        var token = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(token);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler(repository, dbContext, jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.IsRevoked.Should().BeTrue();
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static LogoutCommand CreateCommand() => new("presented-raw-token");

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

    private static LogoutCommandHandler CreateHandler(
        Mock<IAuthTokenRepository> repository,
        Mock<IUnitOfWork>? dbContext = null,
        Mock<IJwtService>? jwtService = null) => new(
        (dbContext ?? new Mock<IUnitOfWork>()).Object,
        repository.Object,
        (jwtService ?? new Mock<IJwtService>()).Object,
        AuthTestData.CreateTimeProvider());
}
