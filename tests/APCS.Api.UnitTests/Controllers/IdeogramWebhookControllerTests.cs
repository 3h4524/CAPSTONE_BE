using System.Text;
using APCS.Api.Controllers;
using APCS.Application.Abstractions.AI;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class IdeogramWebhookControllerTests
{
    private readonly Mock<IImageReadySignals> signals = new();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Controller_AllowsAnonymousAccess()
    {
        typeof(IdeogramWebhookController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task Receive_WakesTheGenerationNamedInTheHeader()
    {
        var action = await Controller("{}").Receive("xtdZiqPwRxqY1Y7NExFmzB", TestContext.CancellationToken);

        action.Should().BeOfType<OkResult>();
        signals.Verify(x => x.Ready("xtdZiqPwRxqY1Y7NExFmzB"), Times.Once);
    }

    [TestMethod]
    public async Task Receive_WithoutTheHeader_TakesTheGenerationFromTheBody()
    {
        await Controller("""{"generation_id":"g-1_a","created":"2026-11-24T12:00:00Z","data":[]}""").Receive(null, TestContext.CancellationToken);

        signals.Verify(x => x.Ready("g-1_a"), Times.Once);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("""{"generation_id":"../../etc"}""")]
    [DataRow("""{"generation_id":42}""")]
    public async Task Receive_WithoutAGenerationId_AnswersOkAndWakesNothing(string body)
    {
        var action = await Controller(body).Receive(null, TestContext.CancellationToken);

        action.Should().BeOfType<OkResult>();
        signals.Verify(x => x.Ready(It.IsAny<string>()), Times.Never);
    }

    private IdeogramWebhookController Controller(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new(signals.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
