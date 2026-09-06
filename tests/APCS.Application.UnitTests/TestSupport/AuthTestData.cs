using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.TestSupport;

internal static class AuthTestData
{
    public static readonly DateTimeOffset UtcNow = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

    public static readonly AccountInfo ActiveUser = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "user@example.com",
        "User Name",
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
            .Returns(new JwtTokenResult("access-token", "new-jwt-id", UtcNow.AddMinutes(15)));
        service.Setup(candidate => candidate.GenerateRefreshToken()).Returns("new-raw-refresh-token");
        service.Setup(candidate => candidate.HashRefreshToken("new-raw-refresh-token"))
            .Returns("new-refresh-token-hash");
        service.Setup(candidate => candidate.GetRefreshTokenExpiresAt(UtcNow))
            .Returns(UtcNow.AddDays(7));
        return service;
    }

    public static Mock<IUnitOfWorkTransaction> CreateTransaction()
    {
        var transaction = new Mock<IUnitOfWorkTransaction>();
        transaction.Setup(candidate => candidate.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return transaction;
    }
}
