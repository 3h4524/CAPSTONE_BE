using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Domain.UnitTests.Entities;

[TestClass]
public sealed class AuthTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void CreateRefreshToken_WithValidValues_InitializesActiveSession()
    {
        var token = CreateToken();

        token.Id.Should().NotBeEmpty();
        token.UserId.Should().Be(UserId);
        token.TokenType.Should().Be(AuthTokenTypes.Refresh);
        token.TokenHash.Should().Be("token-hash");
        token.ExpiresAt.Should().Be(Now.AddDays(7).UtcDateTime);
        token.CreatedAt.Should().Be(Now.UtcDateTime);
        token.IsRevoked.Should().BeFalse();
        token.IsUsed.Should().BeFalse();
        token.IsActive(Now).Should().BeTrue();
    }

    [TestMethod]
    public void CreateRefreshToken_RecordsCallerNetworkDetails()
    {
        var token = AuthToken.CreateRefreshToken(
            UserId,
            "token-hash",
            Now.AddDays(7),
            Now,
            "203.0.113.7",
            "Mozilla/5.0");

        token.CreatedByIp.Should().Be("203.0.113.7");
        token.UserAgent.Should().Be("Mozilla/5.0");
    }

    [TestMethod]
    public void CreateRefreshToken_WithEmptyUserId_Throws()
    {
        var act = () => AuthToken.CreateRefreshToken(
            Guid.Empty,
            "token-hash",
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void CreateRefreshToken_WithBlankTokenHash_Throws(string? tokenHash)
    {
        var act = () => AuthToken.CreateRefreshToken(
            UserId,
            tokenHash!,
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void CreateSingleUseToken_WithRefreshType_Throws()
    {
        var act = () => AuthToken.CreateSingleUseToken(
            UserId,
            AuthTokenTypes.Refresh,
            "token-hash",
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void MarkUsed_MakesSingleUseTokenInactive()
    {
        var token = AuthToken.CreateSingleUseToken(
            UserId,
            AuthTokenTypes.PasswordReset,
            "token-hash",
            Now.AddHours(1),
            Now);

        token.MarkUsed(Now.AddMinutes(5));

        token.IsUsed.Should().BeTrue();
        token.UsedAt.Should().Be(Now.AddMinutes(5).UtcDateTime);
        token.IsActive(Now.AddMinutes(6)).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public void IsExpired_AtBoundary_ReturnsExpected(int secondsAfterExpiry, bool expected)
    {
        var token = CreateToken();
        var comparison = new DateTimeOffset(token.ExpiresAt, TimeSpan.Zero).AddSeconds(secondsAfterExpiry);

        token.IsExpired(comparison).Should().Be(expected);
    }

    [TestMethod]
    public void Revoke_WhenActive_CapturesRevocationTime()
    {
        var token = CreateToken();

        token.Revoke(Now.AddHours(1));

        token.IsRevoked.Should().BeTrue();
        token.IsActive(Now.AddHours(1)).Should().BeFalse();
        token.RevokedAt.Should().Be(Now.AddHours(1).UtcDateTime);
    }

    [TestMethod]
    public void Revoke_WhenAlreadyRevoked_IsIdempotent()
    {
        var token = CreateToken();
        token.Revoke(Now.AddMinutes(1));

        token.Revoke(Now.AddMinutes(2));

        token.RevokedAt.Should().Be(Now.AddMinutes(1).UtcDateTime);
    }

    private static AuthToken CreateToken() => AuthToken.CreateRefreshToken(
        UserId,
        "token-hash",
        Now.AddDays(7),
        Now);
}
