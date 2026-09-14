using System.Text;
using APCS.Api.Controllers;
using APCS.Application.Features.Subscriptions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class PayOsWebhookControllerTests
{
    [TestMethod]
    public void Controller_AllowsAnonymousAccess()
    {
        var attribute = typeof(PayOsWebhookController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false);

        attribute.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task Receive_ForwardsTheRawBodyAndReturnsOk()
    {
        const string body = "{\"orderCode\":123}";
        var service = new Mock<ISubscriptionService>();
        service.Setup(candidate => candidate.HandlePayOsWebhookAsync(body, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var controller = new PayOsWebhookController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var action = await controller.Receive(CancellationToken.None);

        action.Should().BeOfType<OkResult>();
        service.Verify(candidate => candidate.HandlePayOsWebhookAsync(body, It.IsAny<CancellationToken>()), Times.Once);
    }
}
