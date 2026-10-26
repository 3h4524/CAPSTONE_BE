using APCS.Api.Controllers;
using APCS.Application.Features.Commerce;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

[TestClass]
public sealed class MarketOpportunitiesControllerTests
{
    [TestMethod]
    public async Task ProfileRefreshAndList_ForwardRequestsAndCancellation()
    {
        var profile = CreateProfile();
        var opportunity = CreateOpportunity();
        var profileRequest = new SellerResearchProfileRequest(
            "Find personalized gifts",
            ["gift buyers"],
            ["home decor"],
            ["clothing"],
            ["personalized"],
            20,
            100);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IOpportunityService>(MockBehavior.Strict);
        service.Setup(x => x.GetProfileAsync(cancellation.Token)).ReturnsAsync(profile);
        service.Setup(x => x.SaveProfileAsync(profileRequest, cancellation.Token)).ReturnsAsync(profile);
        service.Setup(x => x.RefreshRankingAsync(cancellation.Token)).ReturnsAsync([opportunity]);
        service.Setup(x => x.ListAsync("active", 7, cancellation.Token)).ReturnsAsync([opportunity]);
        var controller = new MarketOpportunitiesController(service.Object, Mock.Of<IOpportunityAnalysisService>());

        (await controller.GetProfile(cancellation.Token)).Should().BeSameAs(profile);
        (await controller.SaveProfile(profileRequest, cancellation.Token)).Should().BeSameAs(profile);
        (await controller.Refresh(cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<IReadOnlyList<SellerOpportunityResponse>>().Which.Should().ContainSingle().Which.Should().BeSameAs(opportunity);
        (await controller.List("active", 7, cancellation.Token)).Should().BeAssignableTo<IReadOnlyList<SellerOpportunityResponse>>().Which.Should().ContainSingle().Which.Should().BeSameAs(opportunity);
        service.VerifyAll();
    }

    [TestMethod]
    public async Task GetUpdateStateAndAnalyze_ReturnFoundResults()
    {
        var id = Guid.NewGuid();
        var opportunity = CreateOpportunity(id);
        var analysis = new OpportunityAnalysisResponse(Guid.NewGuid(), id, "ready", "gemini", "gemini-2.5-flash", "Summary", "Recommendation", ["Support"], ["Risk"], ["Action"], "High confidence");
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IOpportunityService>(MockBehavior.Strict);
        var analysisService = new Mock<IOpportunityAnalysisService>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(id, cancellation.Token)).ReturnsAsync(opportunity);
        service.Setup(x => x.UpdateStateAsync(id, "saved", cancellation.Token)).ReturnsAsync(opportunity);
        analysisService.Setup(x => x.AnalyzeAsync(id, cancellation.Token)).ReturnsAsync(analysis);
        var controller = new MarketOpportunitiesController(service.Object, analysisService.Object);

        (await controller.Get(id, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(opportunity);
        (await controller.UpdateState(id, new("saved"), cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(opportunity);
        (await controller.Analyze(id, cancellation.Token)).Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(analysis);
        service.VerifyAll();
        analysisService.VerifyAll();
    }

    [TestMethod]
    public async Task GetUpdateStateAndAnalyze_WhenMissing_ReturnNotFound()
    {
        var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IOpportunityService>(MockBehavior.Strict);
        var analysisService = new Mock<IOpportunityAnalysisService>(MockBehavior.Strict);
        service.Setup(x => x.GetAsync(id, cancellation.Token)).ReturnsAsync((SellerOpportunityResponse?)null);
        service.Setup(x => x.UpdateStateAsync(id, "dismissed", cancellation.Token)).ReturnsAsync((SellerOpportunityResponse?)null);
        analysisService.Setup(x => x.AnalyzeAsync(id, cancellation.Token)).ReturnsAsync((OpportunityAnalysisResponse?)null);
        var controller = new MarketOpportunitiesController(service.Object, analysisService.Object);

        (await controller.Get(id, cancellation.Token)).Should().BeOfType<NotFoundResult>();
        (await controller.UpdateState(id, new("dismissed"), cancellation.Token)).Should().BeOfType<NotFoundResult>();
        (await controller.Analyze(id, cancellation.Token)).Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
        analysisService.VerifyAll();
    }

    [TestMethod]
    public void Controller_RequiresAuthenticatedUsers()
    {
        typeof(MarketOpportunitiesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Should().ContainSingle(attribute => string.IsNullOrEmpty(attribute.Roles));
    }

    private static SellerResearchProfileResponse CreateProfile() =>
        new(Guid.NewGuid(), "Find personalized gifts", ["gift buyers"], ["home decor"], ["clothing"], ["personalized"], 20, 100, 1, DateTime.UtcNow);

    private static SellerOpportunityResponse CreateOpportunity(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), "etsy", "Personalized gift", "wedding gift", "gifts", "Summary", 80, 75, 90, 84, 1, "active", "Strong fit", new Dictionary<string, double> { ["category"] = .9 }, DateTime.UtcNow);
}
