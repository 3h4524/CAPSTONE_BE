using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanToolsServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly SubscriptionPlan starter = new() { Id = Guid.NewGuid(), Name = "Starter", Tier = "starter", MonthlyPriceUsd = 9.99m, MaxBatchSize = 20, IsActive = true, SortOrder = 1 };
    private readonly SubscriptionPlan pro = new() { Id = Guid.NewGuid(), Name = "Pro", Tier = "pro", MonthlyPriceUsd = 29.99m, MaxBatchSize = 200, IsActive = true, SortOrder = 2 };
    private readonly List<SubscriptionPlan> plans = [];
    private readonly List<PlanFeature> features = [];
    private readonly List<AuditLog> auditLogs = [];
    private readonly Mock<IPlanRepository> planRepository = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();

    public PlanToolsServiceTests()
    {
        plans.AddRange([starter, pro]);
        features.Add(new PlanFeature { PlanId = pro.Id, FeatureCode = PlanFeatures.AiVideo, IsEnabled = true });
        features.Add(new PlanFeature { PlanId = pro.Id, FeatureCode = PlanFeatures.DesignVariations, IsEnabled = true, LimitValue = 4 });
    }

    private static Mock<IRepository<T>> Repository<T>(List<T> items) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(candidate => candidate.Query()).Returns(() => items.AsQueryable().BuildMock());
        repository.Setup(candidate => candidate.AddAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<T, bool, CancellationToken>((item, _, _) => items.Add(item)).Returns(Task.CompletedTask);
        return repository;
    }

    private PlanToolsService CreateService()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.UserId).Returns(AdminId);
        return new PlanToolsService(
            planRepository.Object,
            Repository(plans).Object,
            Repository(features).Object,
            Repository(auditLogs).Object,
            Repository(new List<User> { new() { Id = AdminId, FullName = "Ada Admin", Email = "a@x", AccountStatus = "active" } }).Object,
            new TestFeatureCatalog(),
            unitOfWork.Object,
            currentUser.Object,
            SubscriptionTestData.CreateTimeProvider());
    }

    [TestMethod]
    public async Task DuplicateAsync_CopiesPricesAndFeaturesOffSaleAndLast()
    {
        var result = await CreateService().DuplicateAsync(pro.Id, new DuplicatePlanRequestDto("Pro Plus", "pro-plus"));

        result.IsSuccess.Should().BeTrue();
        var copy = plans.Single(plan => plan.Tier == "pro-plus");
        copy.Should().Match<SubscriptionPlan>(plan =>
            plan.Name == "Pro Plus" && plan.MonthlyPriceUsd == 29.99m && plan.MaxBatchSize == 200 && plan.IsActive == false && plan.SortOrder == 3);
        features.Where(feature => feature.PlanId == copy.Id).Select(feature => (feature.FeatureCode, feature.LimitValue))
            .Should().BeEquivalentTo([(PlanFeatures.AiVideo, (int?)null), (PlanFeatures.DesignVariations, (int?)4)]);
        auditLogs.Single().ActionType.Should().Be("DuplicatePlan");
    }

    [TestMethod]
    public async Task DuplicateAsync_RefusesATakenNameOrABadTier()
    {
        planRepository.Setup(candidate => candidate.IsNameOrTierTakenAsync("Pro", "pro-2", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService();

        (await service.DuplicateAsync(pro.Id, new DuplicatePlanRequestDto("Pro", "pro-2"))).Error.Code.Should().Be(ErrorCodes.SubscriptionPlanNameOrTierTaken);
        (await service.DuplicateAsync(pro.Id, new DuplicatePlanRequestDto("Pro 2", "Pro 2"))).IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReorderAsync_NumbersThePlansInTheOrderGiven()
    {
        var result = await CreateService().ReorderAsync(new ReorderPlansRequestDto([pro.Id, starter.Id]));

        result.IsSuccess.Should().BeTrue();
        pro.SortOrder.Should().Be(1);
        starter.SortOrder.Should().Be(2);
        auditLogs.Single().NewValue.Should().Contain("Pro").And.Contain("Starter");
    }

    [TestMethod]
    public async Task ReorderAsync_RefusesAListMissingAPlan()
    {
        var result = await CreateService().ReorderAsync(new ReorderPlansRequestDto([pro.Id]));

        result.IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetHistoryAsync_DescribesAnEditToAPlanRow()
    {
        auditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), ActorUserId = AdminId, ActionType = PlanChangeActions.Update, ResourceType = "SubscriptionPlan", ResourceId = pro.Id,
            OldValue = """{"name":"Pro","tier":"pro","monthlyPriceUsd":29.99,"annualPriceUsd":299.99,"maxBatchSize":200,"maxConcurrentJobs":5,"isActive":true}""",
            NewValue = """{"name":"Pro","tier":"pro","monthlyPriceUsd":34.99,"annualPriceUsd":299.99,"maxBatchSize":250,"maxConcurrentJobs":5,"isActive":false}""",
            CreatedAt = SubscriptionTestData.UtcNow.UtcDateTime,
        });

        var entry = (await CreateService().GetHistoryAsync(1, 10)).Value.Items.Single();

        entry.Kind.Should().Be("plan");
        entry.PlanName.Should().Be("Pro");
        entry.Summary.Should().Be("Edited Pro: monthly price $29.99 → $34.99, products per run 200 → 250, taken off sale.");
    }

    [TestMethod]
    public async Task GetHistoryAsync_ReturnsOnePageAndTheTotal()
    {
        var at = SubscriptionTestData.UtcNow.UtcDateTime;
        for (var minute = 0; minute < 25; minute++)
        {
            auditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), ActionType = "ReorderPlans", ResourceType = "SubscriptionPlan",
                NewValue = $$"""{"order": ["Plan {{minute}}"]}""", CreatedAt = at.AddMinutes(-minute),
            });
        }
        var service = CreateService();

        var second = (await service.GetHistoryAsync(2, 10)).Value;
        var pastTheEnd = (await service.GetHistoryAsync(9, 10)).Value;

        second.TotalCount.Should().Be(25);
        second.TotalPages.Should().Be(3);
        second.Items.Should().HaveCount(10);
        second.Items[0].Summary.Should().Be("Put the plans in this order: Plan 10.", "page 2 starts at the 11th newest");
        pastTheEnd.PageNumber.Should().Be(3, "a page past the end shows the last one");
        pastTheEnd.Items.Should().HaveCount(5);
    }

    [TestMethod]
    public async Task GetHistoryAsync_DescribesEachChangeNewestFirst()
    {
        var at = SubscriptionTestData.UtcNow.UtcDateTime;
        auditLogs.AddRange(
        [
            new AuditLog
            {
                Id = Guid.NewGuid(), ActorUserId = AdminId, ActionType = "ChangePlanFeature", ResourceType = "SubscriptionPlan", ResourceId = pro.Id,
                OldValue = """{"code": "ai_video", "isEnabled": true, "limitValue": null}""",
                NewValue = """{"code": "ai_video", "isEnabled": false, "limitValue": null, "keptBy": 12, "keptUntil": "2026-12-01"}""",
                CreatedAt = at.AddMinutes(-1),
            },
            new AuditLog
            {
                Id = Guid.NewGuid(), ActorUserId = AdminId, ActionType = "ChangePlanFeature", ResourceType = "SubscriptionPlan", ResourceId = starter.Id,
                OldValue = """{"code": "monthly_listings", "isEnabled": true, "limitValue": 10}""",
                NewValue = """{"code": "monthly_listings", "isEnabled": true, "limitValue": 20, "keptBy": 0}""",
                CreatedAt = at,
            },
            new AuditLog { Id = Guid.NewGuid(), ActionType = "SuspendUser", ResourceType = "User", CreatedAt = at },
        ]);

        var history = (await CreateService().GetHistoryAsync(1, 50)).Value.Items;

        history.Should().HaveCount(2, "only plan changes are listed");
        history[0].Summary.Should().Be("Set Listings per month on Starter from 10 to 20.");
        history[0].Kind.Should().Be("limit");
        history[1].Summary.Should().Be("Removed AI video (Veo) from Pro. 12 subscribers keep the old setting until 1 Dec 2026 at the latest.");
        history[1].ActorName.Should().Be("Ada Admin");
        history[1].PlanName.Should().Be("Pro");
    }

    [TestMethod]
    public async Task GetHistoryAsync_DescribesAChangedRecommendedPlan()
    {
        var at = SubscriptionTestData.UtcNow.UtcDateTime;
        auditLogs.AddRange(
        [
            new AuditLog
            {
                Id = Guid.NewGuid(), ActionType = "SetRecommendedPlan", ResourceType = "PlanSettings",
                OldValue = """{"recommendedPlanTier": "pro"}""", NewValue = """{"recommendedPlanTier": null}""", CreatedAt = at,
            },
            new AuditLog
            {
                Id = Guid.NewGuid(), ActionType = "SetRecommendedPlan", ResourceType = "PlanSettings",
                OldValue = """{"recommendedPlanTier": null}""", NewValue = """{"recommendedPlanTier": "pro"}""", CreatedAt = at.AddMinutes(-1),
            },
        ]);

        var history = (await CreateService().GetHistoryAsync(1, 50)).Value.Items;

        history.Select(entry => entry.Summary).Should().Equal("Stopped recommending a plan.", "Set Pro as the recommended plan.");
    }

    [TestMethod]
    public async Task GetHistoryAsync_DescribesAChangedTrialLength()
    {
        auditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), ActionType = "UpdatePlanSettings", ResourceType = "PlanSettings",
            OldValue = """{"trialPlanTier": "pro", "trialDays": 14}""",
            NewValue = """{"trialPlanTier": "pro", "trialDays": 30}""",
            CreatedAt = SubscriptionTestData.UtcNow.UtcDateTime,
        });

        var history = (await CreateService().GetHistoryAsync(1, 50)).Value.Items;

        history.Single().Summary.Should().Be("Changed Free and trial: the trial now lasts 30 days instead of 14.");
    }
}
