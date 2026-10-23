using APCS.Api.Controllers;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class MarketResearchControllerTests
{
    [TestMethod]
    public async Task RetrieveAndIngest_ForwardsRequestAndReturnsAccepted()
    {
        var request = new MarketResearchRequest(CommercePlatform.Etsy, ["hiking shirt"], 10, 70, 3);
        var response = new MarketResearchResponse(CommercePlatform.Etsy, 10, 3, []);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IMarketResearchService>(MockBehavior.Strict);
        service.Setup(x => x.RetrieveAndIngestAsync(request, cancellation.Token)).ReturnsAsync(response);
        var controller = new MarketResearchController(service.Object, Mock.Of<IMarketResearchScheduleService>());

        var result = (await controller.RetrieveAndIngest(request, cancellation.Token)).Should().BeOfType<AcceptedResult>().Subject;

        result.StatusCode.Should().Be(202);
        result.Value.Should().BeSameAs(response);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task ScheduleEndpoints_ForwardRequestAndCancellation()
    {
        var schedule = new MarketResearchSchedule(true, CommercePlatform.Etsy, ["hiking shirt"], 25, 50, 3, 1440, null, null);
        var request = new UpdateMarketResearchScheduleRequest(true, CommercePlatform.Etsy, ["hiking shirt"], 25, 50, 3, 1440);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IMarketResearchScheduleService>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(cancellation.Token)).ReturnsAsync(schedule);
        service.Setup(x => x.UpdateAsync(request, cancellation.Token)).ReturnsAsync(schedule);
        var controller = new MarketResearchController(Mock.Of<IMarketResearchService>(), service.Object);

        (await controller.GetSchedule(cancellation.Token)).Should().BeSameAs(schedule);
        (await controller.UpdateSchedule(request, cancellation.Token)).Should().BeSameAs(schedule);
        service.VerifyAll();
    }

    [TestMethod]
    public void Controller_RequiresAdminAuthorization()
    {
        var attribute = typeof(MarketResearchController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        attribute.Roles.Should().Be("Admin");
    }
}
