using APCS.Api.Controllers;
using APCS.Api.UnitTests.TestSupport;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class GeneratedMockupSourcesControllerTests
{
    [TestMethod]
    public async Task Import_OwnProduct_ForwardsProductAndCancellationAndReturnsTheSources()
    {
        var productId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var response = new ImportGeneratedMockupsResponse(2, 1, [VideoWorkflowTestData.Mockup()]);
        var service = new Mock<IGeneratedMockupSourceService>(MockBehavior.Strict);
        service.Setup(x => x.ImportAsync(productId, cancellation.Token)).ReturnsAsync(Result.Success(response));

        var result = await new GeneratedMockupSourcesController(service.Object).Import(productId, cancellation.Token);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task Import_ForeignProduct_ReturnsNotFoundProblem()
    {
        var service = new Mock<IGeneratedMockupSourceService>();
        service.Setup(x => x.ImportAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ImportGeneratedMockupsResponse>(Error.NotFound("MockupNotFound", "Product or mockup not found.")));

        var result = (await new GeneratedMockupSourcesController(service.Object).Import(Guid.NewGuid(), default)).Should().BeOfType<ObjectResult>().Subject;

        result.StatusCode.Should().Be(404);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Extensions["code"].Should().Be("MockupNotFound");
    }

    [TestMethod]
    public void Controller_RequiresSellerAuthorization()
    {
        var controller = typeof(GeneratedMockupSourcesController);
        controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles.Should().Be("Seller");
        controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().BeEmpty();
    }
}
