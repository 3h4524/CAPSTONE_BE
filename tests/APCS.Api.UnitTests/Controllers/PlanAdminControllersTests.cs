using System.Runtime.CompilerServices;
using APCS.Api.Controllers;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Admin;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.SellerDashboard;
using APCS.Application.Features.SellerDashboard.Dtos.Response;
using APCS.Common.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace APCS.Api.UnitTests.Controllers;

/// <summary>The thin controllers: each passes its route values on and turns the Result into a response.</summary>
[TestClass]
public sealed class PlanAdminControllersTests
{
    private static readonly Error NotFound = new("Plan.NotFound", "Not found", ErrorType.NotFound);
    private static readonly Guid PlanId = Guid.NewGuid();

    // The controllers never read what the service returns, so an empty instance is enough.
    private static T Dto<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void ShouldBeOk(IActionResult result, object value) =>
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(value);

    private static void ShouldBeNotFound(IActionResult result) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(404);

    [TestMethod]
    public async Task PlanFeatures_Add_PassesThePlanAndRequest()
    {
        var service = new Mock<IPlanFeatureAssignmentService>();
        var request = new AddPlanFeaturesRequestDto([]);
        var changes = new PlanFeatureChangesDto(PlanId, "Pro", []);
        service.Setup(x => x.AddAsync(PlanId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(changes));

        ShouldBeOk(await new AdminPlanFeaturesController(service.Object).Add(PlanId, request, default), changes);
    }

    [TestMethod]
    public async Task PlanFeatures_UpdateLimit_PassesTheNewLimit()
    {
        var service = new Mock<IPlanFeatureAssignmentService>();
        var changes = new PlanFeatureChangesDto(PlanId, "Pro", []);
        service.Setup(x => x.UpdateLimitAsync(PlanId, "design_variations", 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(changes));

        var result = await new AdminPlanFeaturesController(service.Object)
            .UpdateLimit(PlanId, "design_variations", new UpdatePlanFeatureLimitRequestDto(4), default);

        ShouldBeOk(result, changes);
    }

    [TestMethod]
    public async Task PlanFeatures_Remove_WhenThePlanIsMissing_ReturnsNotFound()
    {
        var service = new Mock<IPlanFeatureAssignmentService>();
        service.Setup(x => x.RemoveAsync(PlanId, "ai_video", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PlanFeatureChangesDto>(NotFound));

        ShouldBeNotFound(await new AdminPlanFeaturesController(service.Object).Remove(PlanId, "ai_video", default));
    }

    [TestMethod]
    public async Task PlanFeatures_GetImpact_PassesTheProposedLimit()
    {
        var service = new Mock<IPlanFeatureAssignmentService>();
        var impact = Dto<PlanFeatureImpactDto>();
        service.Setup(x => x.GetImpactAsync(PlanId, "daily_video_cap", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(impact));

        ShouldBeOk(await new AdminPlanFeaturesController(service.Object).GetImpact(PlanId, "daily_video_cap", 10, default), impact);
    }

    [TestMethod]
    public async Task PlanSettings_Get_ReturnsTheSettings()
    {
        var service = new Mock<IPlanSettingsAdminService>();
        var settings = Dto<PlanSettingsDto>();
        service.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(settings));

        ShouldBeOk(await new AdminPlanSettingsController(service.Object).Get(default), settings);
    }

    [TestMethod]
    public async Task PlanSettings_Update_PassesTheRequest()
    {
        var service = new Mock<IPlanSettingsAdminService>();
        var request = Dto<UpdatePlanSettingsRequestDto>();
        var settings = Dto<PlanSettingsDto>();
        service.Setup(x => x.UpdateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(settings));

        ShouldBeOk(await new AdminPlanSettingsController(service.Object).Update(request, default), settings);
    }

    [TestMethod]
    public async Task PlanSettings_SetRecommendedPlan_WhenThePlanIsMissing_ReturnsNotFound()
    {
        var service = new Mock<IPlanSettingsAdminService>();
        var request = new SetRecommendedPlanRequestDto("gone");
        service.Setup(x => x.SetRecommendedPlanAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PlanSettingsDto>(NotFound));

        ShouldBeNotFound(await new AdminPlanSettingsController(service.Object).SetRecommendedPlan(request, default));
    }

    [TestMethod]
    public async Task PlanTools_Duplicate_ReturnsTheNewPlan()
    {
        var service = new Mock<IPlanToolsService>();
        var request = new DuplicatePlanRequestDto("Pro copy", "pro-copy");
        var plan = new DuplicatedPlanDto(Guid.NewGuid(), "Pro copy", "pro-copy");
        service.Setup(x => x.DuplicateAsync(PlanId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(plan));

        ShouldBeOk(await new AdminPlanToolsController(service.Object).Duplicate(PlanId, request, default), plan);
    }

    [TestMethod]
    public async Task PlanTools_Reorder_WhenSaved_ReturnsNoContent()
    {
        var service = new Mock<IPlanToolsService>();
        var request = new ReorderPlansRequestDto([PlanId]);
        service.Setup(x => x.ReorderAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        (await new AdminPlanToolsController(service.Object).Reorder(request, default)).Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task PlanTools_Reorder_WhenRefused_ReturnsTheProblem()
    {
        var service = new Mock<IPlanToolsService>();
        var request = new ReorderPlansRequestDto([PlanId]);
        service.Setup(x => x.ReorderAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(NotFound));

        ShouldBeNotFound(await new AdminPlanToolsController(service.Object).Reorder(request, default));
    }

    [TestMethod]
    public async Task PlanTools_GetHistory_PassesThePage()
    {
        var service = new Mock<IPlanToolsService>();
        var page = Dto<PagedResult<PlanHistoryEntryDto>>();
        service.Setup(x => x.GetHistoryAsync(3, 50, It.IsAny<CancellationToken>(), null)).ReturnsAsync(Result.Success(page));

        ShouldBeOk(await new AdminPlanToolsController(service.Object).GetHistory(3, 50, default), page);
    }

    [TestMethod]
    public async Task Features_GetAll_ReturnsTheCatalog()
    {
        var service = new Mock<IFeatureCatalogAdminService>();
        IReadOnlyList<AdminFeatureDto> catalog = [];
        service.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(catalog));

        ShouldBeOk(await new AdminFeaturesController(service.Object).GetAll(default), catalog);
    }

    [TestMethod]
    public async Task Features_Update_WhenTheFeatureIsMissing_ReturnsNotFound()
    {
        var service = new Mock<IFeatureCatalogAdminService>();
        var request = Dto<UpdateFeatureRequestDto>();
        service.Setup(x => x.UpdateAsync("gone", request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminFeatureDto>(NotFound));

        ShouldBeNotFound(await new AdminFeaturesController(service.Object).Update("gone", request, default));
    }

    [TestMethod]
    public async Task Allowances_Get_WhenSignedOut_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns((Guid?)null);
        var entitlements = new Mock<IPlanEntitlementService>();

        var result = await new AllowancesController(entitlements.Object, currentUser.Object).Get(default);

        result.Should().BeOfType<UnauthorizedResult>();
        entitlements.Verify(x => x.GetAllowancesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Allowances_Get_ReturnsTheSignedInSellersAllowances()
    {
        var userId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        var entitlements = new Mock<IPlanEntitlementService>();
        var summary = new UsageAllowanceSummary("free", null, 5, []);
        entitlements.Setup(x => x.GetAllowancesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(summary);

        ShouldBeOk(await new AllowancesController(entitlements.Object, currentUser.Object).Get(default), summary);
    }

    [TestMethod]
    public async Task SellerDashboard_GetSummary_PassesTheDays()
    {
        var service = new Mock<ISellerDashboardService>();
        var summary = Dto<DashboardSummaryResponseDto>();
        service.Setup(x => x.GetSummaryAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(summary));

        ShouldBeOk(await new SellerDashboardController(service.Object).GetSummaryAsync(7), summary);
    }

    [TestMethod]
    public async Task SellerDashboard_GetActionItems_ReturnsTheItems()
    {
        var service = new Mock<ISellerDashboardService>();
        var items = new DashboardActionItemsResponseDto([]);
        service.Setup(x => x.GetActionItemsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(items));

        ShouldBeOk(await new SellerDashboardController(service.Object).GetActionItemsAsync(), items);
    }

    [TestMethod]
    public async Task AdminDashboard_GetOperations_PassesTheTimeRange()
    {
        var operations = new Mock<IAdminOperationsService>();
        var dto = Dto<AdminOperationsDto>();
        operations.Setup(x => x.GetOperationsAsync("year", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(dto));
        var controller = new AdminDashboardController(Mock.Of<IAdminDashboardService>(), operations.Object);

        ShouldBeOk(await controller.GetOperationsAsync("year"), dto);
    }

    [TestMethod]
    public async Task AdminDashboard_GetOperations_WhenItFails_ReturnsTheProblem()
    {
        var operations = new Mock<IAdminOperationsService>();
        operations.Setup(x => x.GetOperationsAsync("month", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AdminOperationsDto>(NotFound));
        var controller = new AdminDashboardController(Mock.Of<IAdminDashboardService>(), operations.Object);

        ShouldBeNotFound(await controller.GetOperationsAsync());
    }
}
