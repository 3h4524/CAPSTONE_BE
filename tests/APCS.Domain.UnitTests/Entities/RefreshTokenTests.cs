using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Domain.UnitTests.Entities;

[TestClass]
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Create_WithValidValues_InitializesActiveSession()
    {
        var token = CreateToken();

        token.SellerId.Should().Be(42);
        token.TokenHash.Should().Be("token-hash");
        token.JwtId.Should().Be("jwt-id");
        token.ExpiresAtUtc.Should().Be(Now.AddDays(7));
        token.CreatedAtUtc.Should().Be(Now);
        token.IsRevoked.Should().BeFalse();
        token.IsActive(Now).Should().BeTrue();
        token.ConcurrencyStamp.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Create_WithInvalidSellerId_Throws(int sellerId)
    {
        var act = () => RefreshToken.Create(
            sellerId,
            "token-hash",
            "jwt-id",
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    [DataRow(null, "jwt-id")]
    [DataRow("", "jwt-id")]
    [DataRow("token-hash", null)]
    [DataRow("token-hash", " ")]
    public void Create_WithBlankRequiredValue_Throws(string? tokenHash, string? jwtId)
    {
        var act = () => RefreshToken.Create(
            1,
            tokenHash!,
            jwtId!,
            Now.AddDays(1),
            Now);

        act.Should().Throw<ArgumentException>();
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

        token.Revoke(Now.AddHours(1), "Rotated", "new-token-hash");

        token.IsRevoked.Should().BeTrue();
        token.IsActive(Now.AddHours(1)).Should().BeFalse();
        token.RevokedAtUtc.Should().Be(Now.AddHours(1));
        token.ReasonRevoked.Should().Be("Rotated");
        token.ReplacedByTokenHash.Should().Be("new-token-hash");
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

    private static RefreshToken CreateToken() => RefreshToken.Create(
        42,
        "token-hash",
        "jwt-id",
        Now.AddDays(7),
        Now);
}
