using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth;
using APCS.Domain.Entities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.TestSupport;

internal static class AuthTestData
{
    public static readonly DateTimeOffset UtcNow = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

    public static readonly AccountInfoDto ActiveUser = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "user@example.com",
        "User Name",
        true,
        true);

    public static readonly IReadOnlyCollection<string> Roles = new[] { "user" };

    public static FakeTimeProvider CreateTimeProvider() => new(UtcNow);

    public static AuthToken CreateRefreshToken(
        DateTimeOffset? expiresAtUtc = null,
        string tokenHash = "presented-token-hash") => AuthToken.CreateRefreshToken(
        ActiveUser.Id,
        tokenHash,
        expiresAtUtc ?? UtcNow.AddDays(7),
        UtcNow.AddDays(-1));

    public static Mock<IJwtService> CreateJwtService()
    {
        var service = new Mock<IJwtService>();
        service.Setup(candidate => candidate.GenerateAccessToken(
                ActiveUser.Id,
                ActiveUser.Email,
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(new JwtTokenResultDto("access-token", "new-jwt-id", UtcNow.AddMinutes(15)));
        service.Setup(candidate => candidate.GenerateRefreshToken()).Returns("new-raw-refresh-token");
        service.Setup(candidate => candidate.HashRefreshToken("new-raw-refresh-token"))
            .Returns("new-refresh-token-hash");
        service.Setup(candidate => candidate.GetRefreshTokenExpiresAt(UtcNow))
            .Returns(UtcNow.AddDays(7));
        return service;
    }

    /// <summary>
    /// Builds an <see cref="AuthService"/> with loose mocks for every dependency a test does not
    /// override.
    /// </summary>
    public static AuthService CreateService(
        Mock<IAccountService>? accountService = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IAuthTokenRepository>? authTokenRepository = null,
        Mock<IJwtService>? jwtService = null,
        Mock<ICurrentUser>? currentUser = null,
        TimeProvider? timeProvider = null) => new(
        (accountService ?? new Mock<IAccountService>()).Object,
        (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
        (authTokenRepository ?? new Mock<IAuthTokenRepository>()).Object,
        (jwtService ?? CreateJwtService()).Object,
        (currentUser ?? new Mock<ICurrentUser>()).Object,
        timeProvider ?? CreateTimeProvider());
}
