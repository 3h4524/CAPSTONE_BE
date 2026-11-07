using System.Net;
using System.Security.Claims;
using APCS.Api.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace APCS.Api.UnitTests.Extensions;

[TestClass]
public sealed class ApiRateLimitingTests
{
    [TestMethod]
    [DataRow("/health")]
    [DataRow("/internal/media-jobs/claim")]
    [DataRow("/hubs/notification/negotiate")]
    [DataRow("/api/subscriptions/webhooks/payos")]
    [DataRow("/HUBS/support")]
    public void IsExempt_WithInfrastructurePath_ReturnsTrue(string path)
    {
        ApiRateLimiting.IsExempt(new PathString(path)).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("/api/auth/login")]
    [DataRow("/api/batches")]
    [DataRow("/healthz")]
    [DataRow("/")]
    public void IsExempt_WithApplicationPath_ReturnsFalse(string path)
    {
        ApiRateLimiting.IsExempt(new PathString(path)).Should().BeFalse();
    }

    [TestMethod]
    public void GlobalPartitionKey_WithSignedInUser_UsesTheUserId()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Bearer"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.GlobalPartitionKey(context).Should().Be("user:user-1");
    }

    [TestMethod]
    public void GlobalPartitionKey_WithAnonymousCaller_UsesTheClientIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.GlobalPartitionKey(context).Should().Be("ip:203.0.113.7");
    }

    [TestMethod]
    public void GlobalPartitionKey_WithUnauthenticatedIdentityCarryingClaims_UsesTheClientIp()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")]))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.GlobalPartitionKey(context).Should().Be("ip:203.0.113.7");
    }
}
