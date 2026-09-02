using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using APCS.Infrastructure.Options;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class JwtServiceTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private const string SigningKey = "unit-test-signing-key-with-at-least-32-bytes";

    [TestMethod]
    public void GenerateAccessToken_WithValidInput_CreatesValidSignedTokenAndClaims()
    {
        var service = CreateService();

        var result = service.GenerateAccessToken(42, "seller@example.com", ["user", "admin"]);

        result.JwtId.Should().NotBeNullOrWhiteSpace();
        result.ExpiresAtUtc.Should().Be(UtcNow.AddMinutes(15));
        var principal = new JwtSecurityTokenHandler().ValidateToken(
            result.AccessToken,
            CreateValidationParameters(),
            out var validatedToken);
        validatedToken.Should().BeOfType<JwtSecurityToken>()
            .Which.Header.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
        principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("42");
        principal.FindFirstValue(ClaimTypes.Email).Should().Be("seller@example.com");
        principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)
            .Should().BeEquivalentTo("user", "admin");
        principal.FindFirstValue(JwtRegisteredClaimNames.Jti).Should().Be(result.JwtId);
    }

    [TestMethod]
    public void GenerateAccessToken_CalledTwice_CreatesDifferentIdentifiers()
    {
        var service = CreateService();

        var first = service.GenerateAccessToken(42, "seller@example.com", ["user"]);
        var second = service.GenerateAccessToken(42, "seller@example.com", ["user"]);

        first.JwtId.Should().NotBe(second.JwtId);
        first.AccessToken.Should().NotBe(second.AccessToken);
    }

    [TestMethod]
    public void GenerateRefreshToken_CalledTwice_ReturnsBase64UrlSecretsWithExpectedEntropy()
    {
        var service = CreateService();

        var first = service.GenerateRefreshToken();
        var second = service.GenerateRefreshToken();

        first.Should().NotBe(second);
        first.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        first.Length.Should().BeGreaterThanOrEqualTo(80);
    }

    [TestMethod]
    public void HashAndExpiration_DelegateToStableSecurityRules()
    {
        var service = CreateService();

        service.HashRefreshToken("abc")
            .Should().Be("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=");
        service.GetRefreshTokenExpiresAt(UtcNow).Should().Be(UtcNow.AddDays(7));
    }

    [TestMethod]
    public void GenerateAccessToken_WithBlankEmail_Throws()
    {
        var act = () => CreateService().GenerateAccessToken(42, " ", ["user"]);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void GenerateAccessToken_WithNullRoles_Throws()
    {
        var act = () => CreateService().GenerateAccessToken(42, "seller@example.com", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static JwtService CreateService() => new(
        Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = "APCS.UnitTests",
            Audience = "APCS.UnitTests.Client",
            SigningKey = SigningKey,
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        }),
        new FakeTimeProvider(UtcNow));

    private static TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = "APCS.UnitTests",
        ValidateAudience = true,
        ValidAudience = "APCS.UnitTests.Client",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateLifetime = false,
        ClockSkew = TimeSpan.Zero
    };
}
