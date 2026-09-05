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

        token.UserId.Should().Be(UserId);
        token.TokenType.Should().Be(AuthTokenTypes.Refresh);
        token.TokenHash.Should().Be("token-hash");
        token.JwtId.Should().Be("jwt-id");
        token.ExpiresAtUtc.Should().Be(Now.AddDays(7));
        token.CreatedAtUtc.Should().Be(Now);
        token.IsRevoked.Should().BeFalse();
        token.IsUsed.Should().BeFalse();
        token.IsActive(Now).Should().BeTrue();
        token.ConcurrencyStamp.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void CreateRefreshToken_RecordsTheCallerNetworkDetails()
    {
        var token = AuthToken.CreateRefreshToken(
            UserId,
            "token-hash",
            "jwt-id",
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
            "jwt-id",
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    [DataRow(null, "jwt-id")]
    [DataRow("", "jwt-id")]
    [DataRow("token-hash", null)]
    [DataRow("token-hash", " ")]
    public void CreateRefreshToken_WithBlankRequiredValue_Throws(string? tokenHash, string? jwtId)
    {
        var act = () => AuthToken.CreateRefreshToken(
            UserId,
            tokenHash!,
            jwtId!,
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
    public void MarkUsed_MakesASingleUseTokenInactive()
    {
        var token = AuthToken.CreateSingleUseToken(
            UserId,
            AuthTokenTypes.PasswordReset,
            "token-hash",
            Now.AddHours(1),
            Now);

        token.MarkUsed(Now.AddMinutes(5));

        token.IsUsed.Should().BeTrue();
        token.UsedAtUtc.Should().Be(Now.AddMinutes(5));
        token.IsActive(Now.AddMinutes(6)).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(1, true)]
    public void IsExpired_AtBoundary_ReturnsExpected(int secondsAfterExpiry, bool expected)
    {
        var token = CreateToken();

        token.IsExpired(token.ExpiresAtUtc.AddSeconds(secondsAfterExpiry)).Should().Be(expected);
    }

    [TestMethod]
    public void Revoke_WhenActive_CapturesRevocationAndRotationValues()
    {
        var token = CreateToken();
        var originalStamp = token.ConcurrencyStamp;

        token.Revoke(Now.AddHours(1), "Rotated", "new-token-hash", "203.0.113.7");

        token.IsRevoked.Should().BeTrue();
        token.IsActive(Now.AddHours(1)).Should().BeFalse();
        token.RevokedAtUtc.Should().Be(Now.AddHours(1));
        token.ReasonRevoked.Should().Be("Rotated");
        token.ReplacedByTokenHash.Should().Be("new-token-hash");
        token.RevokedByIp.Should().Be("203.0.113.7");
        token.ConcurrencyStamp.Should().NotBe(originalStamp);
    }

    [TestMethod]
    public void Revoke_WhenAlreadyRevoked_IsIdempotent()
    {
        var token = CreateToken();
        token.Revoke(Now.AddMinutes(1), "Logout");
        var stamp = token.ConcurrencyStamp;

        token.Revoke(Now.AddMinutes(2), "Rotated", "replacement");

        token.RevokedAtUtc.Should().Be(Now.AddMinutes(1));
        token.ReasonRevoked.Should().Be("Logout");
        token.ReplacedByTokenHash.Should().BeNull();
        token.ConcurrencyStamp.Should().Be(stamp);
    }

    [TestMethod]
    public void Revoke_WithBlankReason_Throws()
    {
        var token = CreateToken();

        var act = () => token.Revoke(Now, " ");

        act.Should().Throw<ArgumentException>();
    }

    private static AuthToken CreateToken() => AuthToken.CreateRefreshToken(
        UserId,
        "token-hash",
        "jwt-id",
        Now.AddDays(7),
        Now);
}
