using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CurrentUserServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid FallbackUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void Properties_WithStandardClaims_ReturnExpectedValues()
    {
        var service = CreateService(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Email, "user@example.com"));

        service.UserId.Should().Be(UserId);
        service.Email.Should().Be("user@example.com");
        service.IsAuthenticated.Should().BeTrue();
    }

    [TestMethod]
    public void Properties_WithJwtFallbackClaims_ReturnExpectedValues()
    {
        var service = CreateService(
            new Claim(JwtRegisteredClaimNames.Sub, FallbackUserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, "jwt@example.com"));

        service.UserId.Should().Be(FallbackUserId);
        service.Email.Should().Be("jwt@example.com");
    }

    [TestMethod]
    public void UserId_WithMalformedClaim_ReturnsNull()
    {
        CreateService(new Claim(ClaimTypes.NameIdentifier, "not-a-guid"))
            .UserId.Should().BeNull();
    }

    [TestMethod]
    public void Properties_WithoutHttpContext_ReturnDefaults()
    {
        var service = new CurrentUserService(new HttpContextAccessor());

        service.UserId.Should().BeNull();
        service.Email.Should().BeNull();
        service.IsAuthenticated.Should().BeFalse();
    }

    private static CurrentUserService CreateService(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };

        return new CurrentUserService(new HttpContextAccessor { HttpContext = context });
    }
}
