using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Application.Features.Auth.Commands.RefreshToken;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RefreshTokenEntity = APCS.Domain.Entities.RefreshToken;

namespace APCS.Application.UnitTests.Features.Auth.Commands.RefreshToken;

[TestClass]
public sealed class RefreshTokenCommandHandlerTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task Handle_WhenTokenIsMissing_ReturnsMissingFailure(string? refreshToken)
    {
        var repository = new Mock<IRefreshTokenRepository>();
        var jwtService = new Mock<IJwtService>();

        var result = await CreateHandler(repository, jwtService: jwtService).Handle(
            new RefreshTokenCommand(refreshToken),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenMissing);
        jwtService.Verify(service => service.HashRefreshToken(It.IsAny<string>()), Times.Never);
        repository.Verify(service => service.GetByHashAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenTokenDoesNotExist_ReturnsInvalidFailure()
    {
        var (repository, jwtService) = CreateLookup(null);

        var result = await CreateHandler(repository, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenInvalid);
    }

    [TestMethod]
    public async Task Handle_WhenTokenWasRevoked_ReturnsReusedFailureWithoutSaving()
    {
        var token = AuthTestData.CreateRefreshToken();
        token.Revoke(AuthTestData.UtcNow.AddMinutes(-1), "Logout");
        var (repository, jwtService) = CreateLookup(token);
        var dbContext = new Mock<IUnitOfWork>();

        var result = await CreateHandler(repository, dbContext, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_WhenTokenExpired_RevokesAndReturnsExpiredFailure()
    {
        var token = AuthTestData.CreateRefreshToken(AuthTestData.UtcNow);
        var (repository, jwtService) = CreateLookup(token);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler(repository, dbContext, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenExpired);
        token.IsRevoked.Should().BeTrue();
        token.ReasonRevoked.Should().Be("Expired");
        dbContext.Verify(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WhenExpiredTokenSaveConflicts_ReturnsReusedFailure()
    {
        var token = AuthTestData.CreateRefreshToken(AuthTestData.UtcNow);
        var (repository, jwtService) = CreateLookup(token);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler(repository, dbContext, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
    }

    [TestMethod]
    public async Task Handle_WhenOwningUserDoesNotExist_ReturnsInvalidFailure()
    {
        var (repository, jwtService) = CreateLookup(AuthTestData.CreateRefreshToken());
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUserInfo?)null);

        var result = await CreateHandler(repository, identity: identity, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenInvalid);
    }

    [TestMethod]
    [DataRow(false, false, "auth.user_inactive")]
    [DataRow(true, true, "auth.user_locked_out")]
    public async Task Handle_WhenUserCannotAuthenticate_ReturnsExpectedFailure(
        bool isActive,
        bool isLockedOut,
        string expectedCode)
    {
        var (repository, jwtService) = CreateLookup(AuthTestData.CreateRefreshToken());
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser with
            {
                IsActive = isActive,
                IsLockedOut = isLockedOut
            });

        var result = await CreateHandler(repository, identity: identity, jwtService: jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(expectedCode);
    }

    [TestMethod]
    public async Task Handle_WithActiveToken_RotatesSessionAndReturnsNewTokens()
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var existingToken = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(existingToken, cancellationToken);
        RefreshTokenEntity? addedToken = null;
        repository.Setup(candidate => candidate.Add(It.IsAny<RefreshTokenEntity>()))
            .Callback<RefreshTokenEntity>(token => addedToken = token);
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.ActiveUser);
        identity.Setup(service => service.GetRolesAsync(AuthTestData.ActiveUser.Id, cancellationToken))
            .ReturnsAsync(AuthTestData.Roles);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(cancellationToken)).ReturnsAsync(2);
        SetupNewSession(jwtService);
        var handler = CreateHandler(repository, dbContext, identity, jwtService);

        var result = await handler.Handle(CreateCommand(), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access-token");
        result.Value.RefreshToken.Should().Be("new-raw-refresh-token");
        existingToken.IsRevoked.Should().BeTrue();
        existingToken.ReasonRevoked.Should().Be("Rotated");
        existingToken.ReplacedByTokenHash.Should().Be("new-refresh-token-hash");
        addedToken!.TokenHash.Should().Be("new-refresh-token-hash");
        dbContext.Verify(context => context.SaveChangesAsync(cancellationToken), Times.Once);
    }

    [TestMethod]
    public async Task Handle_WhenRotationSaveConflicts_ReturnsReusedFailure()
    {
        var existingToken = AuthTestData.CreateRefreshToken();
        var (repository, jwtService) = CreateLookup(existingToken);
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindByIdAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.ActiveUser);
        identity.Setup(service => service.GetRolesAsync(
                AuthTestData.ActiveUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestData.Roles);
        SetupNewSession(jwtService);
        var dbContext = new Mock<IUnitOfWork>();
        dbContext.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler(repository, dbContext, identity, jwtService).Handle(
            CreateCommand(),
            CancellationToken.None);

        result.Error.Code.Should().Be(ErrorCodes.RefreshTokenReused);
    }

    private static RefreshTokenCommand CreateCommand() => new("presented-raw-token");

    private static (Mock<IRefreshTokenRepository> Repository, Mock<IJwtService> JwtService) CreateLookup(
        RefreshTokenEntity? token,
        CancellationToken cancellationToken = default)
    {
        var repository = new Mock<IRefreshTokenRepository>();
        repository.Setup(candidate => candidate.GetByHashAsync("presented-token-hash", cancellationToken))
            .ReturnsAsync(token);
        var jwtService = new Mock<IJwtService>();
        jwtService.Setup(service => service.HashRefreshToken("presented-raw-token"))
            .Returns("presented-token-hash");
        return (repository, jwtService);
    }

    private static void SetupNewSession(Mock<IJwtService> jwtService)
    {
        var configured = AuthTestData.CreateJwtService();
        jwtService.Setup(service => service.GenerateAccessToken(
                AuthTestData.ActiveUser.Id,
                AuthTestData.ActiveUser.Email,
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(() => configured.Object.GenerateAccessToken(
                AuthTestData.ActiveUser.Id,
                AuthTestData.ActiveUser.Email,
                AuthTestData.Roles));
        jwtService.Setup(service => service.GenerateRefreshToken()).Returns("new-raw-refresh-token");
        jwtService.Setup(service => service.HashRefreshToken("new-raw-refresh-token"))
            .Returns("new-refresh-token-hash");
        jwtService.Setup(service => service.GetRefreshTokenExpiresAt(AuthTestData.UtcNow))
            .Returns(AuthTestData.UtcNow.AddDays(7));
    }

    private static RefreshTokenCommandHandler CreateHandler(
        Mock<IRefreshTokenRepository> repository,
        Mock<IUnitOfWork>? dbContext = null,
        Mock<IIdentityService>? identity = null,
        Mock<IJwtService>? jwtService = null) => new(
        (dbContext ?? new Mock<IUnitOfWork>()).Object,
        repository.Object,
        (identity ?? new Mock<IIdentityService>()).Object,
        (jwtService ?? new Mock<IJwtService>()).Object,
        AuthTestData.CreateTimeProvider());
}
