using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using APCS.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class CurrentUserServiceTests
{
    [TestMethod]
    public void Properties_WithStandardClaims_ReturnExpectedValues()
    {
        var service = CreateService(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Email, "seller@example.com"));

        service.UserId.Should().Be(42);
        service.Email.Should().Be("seller@example.com");
        service.IsAuthenticated.Should().BeTrue();
    }

    [TestMethod]
    public void Properties_WithJwtFallbackClaims_ReturnExpectedValues()
    {
        var service = CreateService(
            new Claim(JwtRegisteredClaimNames.Sub, "7"),
            new Claim(JwtRegisteredClaimNames.Email, "jwt@example.com"));

        service.UserId.Should().Be(7);
        service.Email.Should().Be("jwt@example.com");
    }

    [TestMethod]
    public void UserId_WithMalformedClaim_ReturnsNull()
    {
        CreateService(new Claim(ClaimTypes.NameIdentifier, "not-an-integer"))
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
