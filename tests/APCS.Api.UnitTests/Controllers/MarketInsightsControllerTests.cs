using APCS.Api.Controllers;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Commerce;
using APCS.Common.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class MarketInsightsControllerTests
{
    [TestMethod]
    public async Task Collect_ForwardsKeywordAndReturnsAccepted()
    {
        var request = new CollectMarketInsightRequest("wedding gift");
        var response = new MarketResearchJobStatus(
            Guid.NewGuid(),
            request.Query,
            "queued",
            0,
            0,
            null,
            DateTimeOffset.UtcNow,
            null);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IMarketInsightsService>(MockBehavior.Strict);
        service
            .Setup(x => x.QueueCollectionAsync(request.Query, cancellation.Token))
            .ReturnsAsync(response);
        var controller = new MarketInsightsController(service.Object, Mock.Of<IMarketInsightScheduleService>());

        var result = (await controller.Collect(request, cancellation.Token))
            .Should()
            .BeOfType<AcceptedResult>()
            .Subject;

        result.StatusCode.Should().Be(202);
        result.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task GetCollectionStatus_WhenJobExistsReturnsOk()
    {
        var jobId = Guid.NewGuid();
        var response = new MarketResearchJobStatus(
            jobId,
            "wedding gift",
            "running",
            0,
            0,
            null,
            DateTimeOffset.UtcNow,
            null);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IMarketInsightsService>(MockBehavior.Strict);
        service
            .Setup(x => x.GetCollectionStatusAsync(jobId, cancellation.Token))
            .ReturnsAsync(response);
        var controller = new MarketInsightsController(service.Object, Mock.Of<IMarketInsightScheduleService>());

        var result = (await controller.GetCollectionStatus(jobId, cancellation.Token))
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject;

        result.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public void Controller_IsRestrictedToAdministrators()
    {
        var attribute = typeof(MarketInsightsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        // Raw metric inspection is an admin tool; sellers use Market Radar instead.
        attribute.Roles.Should().Be(AuthConstants.AdminRole);
    }
}
