using APCS.Api.Controllers;
using APCS.Application.Features.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class RagBenchmarkControllerTests
{
    [TestMethod]
    public void Controller_IsRestrictedToAdministrators()
    {
        typeof(RagBenchmarkController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single()
            .Roles.Should().Be(AuthConstants.AdminRole);
    }

    [TestMethod]
    public async Task RunKnownItem_ReturnsTheBenchmark()
    {
        var response = new RagKnownItemBenchmarkResponse("set", 1, 5, DateTimeOffset.UnixEpoch, false, 10,
            new RagEvaluationResponse("run", DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, 0, []));
        var service = new Mock<IRagBenchmarkService>();
        service.Setup(x => x.RunKnownItemAsync(It.IsAny<RagKnownItemBenchmarkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var action = await new RagBenchmarkController(service.Object).RunKnownItem(new RagKnownItemBenchmarkRequest(), CancellationToken.None);

        action.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(response);
    }

    [TestMethod]
    public async Task RunKnownItem_ReturnsNotFoundForAnUnknownCaseSet()
    {
        var service = new Mock<IRagBenchmarkService>();
        service.Setup(x => x.RunKnownItemAsync(It.IsAny<RagKnownItemBenchmarkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagKnownItemBenchmarkResponse?)null);

        var action = await new RagBenchmarkController(service.Object)
            .RunKnownItem(new RagKnownItemBenchmarkRequest(CaseSetId: "gone"), CancellationToken.None);

        action.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task RunKnownItem_ReturnsBadRequestWhenNoCaseCanBeBuilt()
    {
        var service = new Mock<IRagBenchmarkService>();
        service.Setup(x => x.RunKnownItemAsync(It.IsAny<RagKnownItemBenchmarkRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no listings"));

        var action = await new RagBenchmarkController(service.Object).RunKnownItem(new RagKnownItemBenchmarkRequest(), CancellationToken.None);

        action.Should().BeOfType<BadRequestObjectResult>();
    }
}
