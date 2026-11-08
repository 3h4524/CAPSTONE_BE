using System.Net;
using System.Reflection;
using System.Security.Claims;
using APCS.Api.Controllers;
using APCS.Api.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace APCS.Api.UnitTests.Extensions;

[TestClass]
public sealed class ApiRateLimitingTests
{
    [TestMethod]
    [DataRow(typeof(InternalMediaJobsController))]
    [DataRow(typeof(InternalAiShotController))]
    [DataRow(typeof(PayOsWebhookController))]
    public void Controller_CalledByAMachine_OptsOutOfRateLimiting(Type controller)
    {
        controller.GetCustomAttribute<DisableRateLimitingAttribute>().Should().NotBeNull();
    }

    [TestMethod]
    public void AssistantChat_UsesTheAssistantChatPolicy()
    {
        var chat = typeof(AssistantController).GetMethods()
            .Select(method => method.GetCustomAttribute<EnableRateLimitingAttribute>())
            .Where(attribute => attribute is not null)
            .Should().ContainSingle().Subject!;

        chat.PolicyName.Should().Be(ApiRateLimiting.AssistantChatPolicy);
    }

    [TestMethod]
    public void CallerKey_WithSignedInUser_UsesTheUserId()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Bearer"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.CallerKey(context).Should().Be("user:user-1");
    }

    [TestMethod]
    public void CallerKey_WithAnonymousCaller_UsesTheClientIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.CallerKey(context).Should().Be("ip:203.0.113.7");
    }

    [TestMethod]
    public void CallerKey_WithUnauthenticatedIdentityCarryingClaims_UsesTheClientIp()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")]))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        ApiRateLimiting.CallerKey(context).Should().Be("ip:203.0.113.7");
    }
}
