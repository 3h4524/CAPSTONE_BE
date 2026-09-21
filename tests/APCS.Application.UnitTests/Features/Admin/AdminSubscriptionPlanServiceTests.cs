using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Validators;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class AdminSubscriptionPlanServiceTests
{
    private static AdminSubscriptionPlanService CreateService(
        Mock<IPlanRepository>? plans = null,
        Mock<ISubscriptionRepository>? subscriptions = null,
        Mock<IRepository<PlanFeature>>? planFeatures = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        TimeProvider? timeProvider = null)
    {
        var planFeaturesMock = planFeatures ?? CreateEmptyPlanFeaturesMock();
        var unitOfWorkMock = unitOfWork ?? new Mock<IUnitOfWork>();

        return new AdminSubscriptionPlanService(
            (plans ?? new Mock<IPlanRepository>()).Object,
            (subscriptions ?? new Mock<ISubscriptionRepository>()).Object,
            planFeaturesMock.Object,
            unitOfWorkMock.Object,
            timeProvider ?? TimeProvider.System,
            new CreatePlanRequestDtoValidator(),
            new UpdatePlanRequestDtoValidator(),
            new DeletePlanRequestDtoValidator());
    }

    private static Mock<IRepository<PlanFeature>> CreateEmptyPlanFeaturesMock()
    {
        var mock = new Mock<IRepository<PlanFeature>>();
        mock.Setup(r => r.Query()).Returns(new List<PlanFeature>().AsQueryable().BuildMock());
        return mock;
    }

    private static CreatePlanRequestDto ValidCreateRequest() => new(
        Name: "Creator",
        Tier: "creator",
        Description: "For growing sellers",
        MonthlyPriceUsd: 49m,
        AnnualPriceUsd: 490m,
        MaxBatchSize: 200,
        MaxConcurrentJobs: 2,
        MaxProductsPerMonth: 2000,
        ImageGenerationQuota: 1000,
        VideoGenerationQuota: 100,
        ApiCallQuota: 20000,
        StorageQuotaGb: 200m,
        CustomApiKeysAllowed: true,
        WhiteLabelExportEnabled: true,
        PrioritySupport: false,
        IsActive: true);

    private static UpdatePlanRequestDto ValidUpdateRequest() => new(
        Name: "Creator Plus",
        Description: "Updated",
        MonthlyPriceUsd: 59m,
        AnnualPriceUsd: 590m,
        MaxBatchSize: 250,
        MaxConcurrentJobs: 3,
        MaxProductsPerMonth: 2500,
        ImageGenerationQuota: 1200,
        VideoGenerationQuota: 120,
        ApiCallQuota: 25000,
        StorageQuotaGb: 250m,
        CustomApiKeysAllowed: true,
        WhiteLabelExportEnabled: false,
        PrioritySupport: true,
        IsActive: true);

    [TestMethod]
    public async Task CreateAsync_WhenValid_AddsPlanAndSaves()
    {
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.IsNameOrTierTakenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = CreateService(plansRepo, unitOfWork: unitOfWork);

        var result = await service.CreateAsync(ValidCreateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Creator");
        result.Value.Tier.Should().Be("creator");
        result.Value.WhiteLabelExportEnabled.Should().BeTrue();
        result.Value.CanDelete.Should().BeTrue();
        plansRepo.Verify(r => r.AddAsync(
            It.Is<SubscriptionPlan>(plan => plan.Name == "Creator" && plan.Tier == "creator"),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WhenNameOrTierAlreadyTaken_ReturnsFailure()
    {
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.IsNameOrTierTakenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(plansRepo);

        var result = await service.CreateAsync(ValidCreateRequest());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("subscriptions.plan_name_or_tier_taken");
        plansRepo.Verify(r => r.AddAsync(It.IsAny<SubscriptionPlan>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_WhenNameMissing_ReturnsValidationFailure()
    {
        var service = CreateService();
        var request = ValidCreateRequest() with { Name = string.Empty };

        var result = await service.CreateAsync(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.failed");
    }

    [TestMethod]
    public async Task CreateAsync_WhenTierHasUppercaseOrSpaces_ReturnsValidationFailure()
    {
        var service = CreateService();
        var request = ValidCreateRequest() with { Tier = "Creator Plus" };

        var result = await service.CreateAsync(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.failed");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenPlanNotFound_ReturnsFailure()
    {
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.GetByIdForAdminAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPlan?)null);
        var service = CreateService(plansRepo);

        var result = await service.UpdateAsync(Guid.NewGuid(), ValidUpdateRequest());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("subscriptions.plan_not_found");
    }

    [TestMethod]
    public async Task UpdateAsync_TierSlugIsNeverChanged()
    {
        var planId = Guid.NewGuid();
        var existingPlan = new SubscriptionPlan
        {
            Id = planId,
            Name = "Creator",
            Tier = "creator",
            PlanFeatures = new List<PlanFeature>()
        };
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.GetByIdForAdminAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPlan);
        plansRepo.Setup(r => r.IsNameOrTierTakenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(plansRepo);

        var result = await service.UpdateAsync(planId, ValidUpdateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Tier.Should().Be("creator");
        existingPlan.Tier.Should().Be("creator");
    }

    [TestMethod]
    public async Task DeleteAsync_WhenPlanHasNoSubscriptionHistory_HardDeletes()
    {
        var planId = Guid.NewGuid();
        var plan = new SubscriptionPlan { Id = planId, Name = "Creator", Tier = "creator", PlanFeatures = new List<PlanFeature>() };
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.GetByIdForAdminAsync(planId, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var subscriptionsRepo = new Mock<ISubscriptionRepository>();
        subscriptionsRepo.Setup(r => r.HasAnySubscriptionReferenceAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = CreateService(plansRepo, subscriptionsRepo);

        var result = await service.DeleteAsync(planId, new DeletePlanRequestDto("No longer offered"));

        result.IsSuccess.Should().BeTrue();
        plansRepo.Verify(r => r.RemoveAsync(plan, false, It.IsAny<CancellationToken>()), Times.Once);
        plansRepo.Verify(r => r.UpdateAsync(It.IsAny<SubscriptionPlan>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenPlanHasSubscriptionHistory_ReturnsConflictAndNeverDeactivates()
    {
        // The Delete action no longer falls back to soft-deactivate on its own — deactivating
        // must be a deliberate, separate action via UpdateAsync's "Plan is active" toggle.
        var planId = Guid.NewGuid();
        var plan = new SubscriptionPlan { Id = planId, Name = "Creator", Tier = "creator", IsActive = true, PlanFeatures = new List<PlanFeature>() };
        var plansRepo = new Mock<IPlanRepository>();
        plansRepo.Setup(r => r.GetByIdForAdminAsync(planId, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var subscriptionsRepo = new Mock<ISubscriptionRepository>();
        subscriptionsRepo.Setup(r => r.HasAnySubscriptionReferenceAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = CreateService(plansRepo, subscriptionsRepo);

        var result = await service.DeleteAsync(planId, new DeletePlanRequestDto("Retiring this tier"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("subscriptions.plan_has_subscription_history");
        plan.IsActive.Should().BeTrue();
        plansRepo.Verify(r => r.UpdateAsync(It.IsAny<SubscriptionPlan>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        plansRepo.Verify(r => r.RemoveAsync(It.IsAny<SubscriptionPlan>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenReasonMissing_ReturnsValidationFailure()
    {
        var service = CreateService();

        var result = await service.DeleteAsync(Guid.NewGuid(), new DeletePlanRequestDto(string.Empty));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.failed");
    }
}
