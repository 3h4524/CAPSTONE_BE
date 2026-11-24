using System.Text;
using System.Text.Json;
using APCS.Api.Controllers;
using APCS.Application.Features.Workflows;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class MiniMaxCallbackControllerTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private readonly Mock<IAiVideoCallbackService> service = new();

    [TestMethod]
    public void Controller_AllowsAnonymousAccess()
    {
        typeof(MiniMaxCallbackController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task Receive_UrlCheck_AnswersWithTheChallenge()
    {
        const string body = """{"challenge":"abc-123"}""";
        service.Setup(x => x.ReceiveAsync(JobId, 0, "sig", body, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiVideoCallbackResult(AiVideoCallbackOutcome.Challenge, "abc-123"));

        var action = await Controller(body).Receive(JobId, 0, "sig", CancellationToken.None);

        var ok = action.Should().BeOfType<OkObjectResult>().Subject;
        JsonSerializer.Serialize(ok.Value).Should().Be("""{"challenge":"abc-123"}""");
    }

    [TestMethod]
    public async Task Receive_TaskUpdate_ReturnsOk()
    {
        service.Setup(x => x.ReceiveAsync(JobId, 0, "sig", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiVideoCallbackResult(AiVideoCallbackOutcome.Accepted));

        (await Controller("{}").Receive(JobId, 0, "sig", CancellationToken.None)).Should().BeOfType<OkResult>();
    }

    [TestMethod]
    public async Task Receive_UrlItDidNotHandOut_ReturnsNotFound()
    {
        service.Setup(x => x.ReceiveAsync(JobId, 0, "forged", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiVideoCallbackResult(AiVideoCallbackOutcome.Unknown));

        (await Controller("{}").Receive(JobId, 0, "forged", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
    }

    private MiniMaxCallbackController Controller(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new(service.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
