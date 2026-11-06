using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanSettingsAdminServiceTests
{
    private readonly List<UsageAllowance> allowanceRows = [];
    private readonly List<PlanSetting> settingRows = [];
    private readonly List<AuditLog> auditLogs = [];
    private readonly List<SubscriptionPlan> plans =
    [
        new() { Id = Guid.NewGuid(), Name = "Starter", Tier = "starter", MonthlyPriceUsd = 9.99m, MaxBatchSize = 20, IsActive = true },
        new() { Id = Guid.NewGuid(), Name = "Pro", Tier = "pro", MonthlyPriceUsd = 29.99m, MaxBatchSize = 200, IsActive = true },
        new() { Id = Guid.NewGuid(), Name = "Old", Tier = "old", MonthlyPriceUsd = 5m, MaxBatchSize = 5, IsActive = false },
    ];
    private readonly Mock<IUnitOfWork> unitOfWork = new();

    private static Mock<IRepository<T>> Repository<T>(List<T> items) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(candidate => candidate.Query()).Returns(() => items.AsQueryable().BuildMock());
        repository.Setup(candidate => candidate.AddAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<T, bool, CancellationToken>((item, _, _) => items.Add(item)).Returns(Task.CompletedTask);
        return repository;
    }

    // The real settings reader over the same rows, so a save shows on the next read.
    private PlanSettings Settings() => new(
        Repository(allowanceRows).Object,
        Repository(settingRows).Object,
        Options.Create(new UsageAllowanceOptions()),
        new PlanSettingsCache(TimeProvider.System),
        NullLogger<PlanSettings>.Instance);

    private PlanSettingsAdminService CreateService()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.UserId).Returns(Guid.NewGuid());
        return new PlanSettingsAdminService(
            Settings(),
            Options.Create(new UsageAllowanceOptions()),
            Repository(allowanceRows).Object,
            Repository(settingRows).Object,
            Repository(plans).Object,
            Repository(new List<Subscription>()).Object,
            Repository(new List<User>()).Object,
            Repository(auditLogs).Object,
            unitOfWork.Object,
            currentUser.Object,
            SubscriptionTestData.CreateTimeProvider());
    }

    private static UpdatePlanSettingsRequestDto Request(string tier = "pro", params AllowanceValueDto[] values) => new(tier, values);

    [TestMethod]
    public async Task GetAsync_WithNoRows_ShowsTheShippedDefaultsAndPlansOnSale()
    {
        var settings = (await CreateService().GetAsync()).Value;

        settings.TrialPlanTier.Should().Be("pro");
        settings.TrialPlans.Select(plan => plan.Tier).Should().Equal("starter", "pro");
        settings.TrialDays.Should().Be(14);
        settings.Allowances.Single(item => item.Scope == "free" && item.Code == "mockups").Should()
            .Match<AllowanceSettingDto>(item => item.Limit == 6 && item.Default == 6);
    }

    [TestMethod]
    public async Task UpdateAsync_SavesTheTrialPlanAndAllowancesAndShowsThemNext()
    {
        var result = await CreateService().UpdateAsync(Request("starter",
            new AllowanceValueDto("free", "mockups", 4),
            new AllowanceValueDto("trial", "listings", 30)));

        result.IsSuccess.Should().BeTrue();
        result.Value.TrialPlanTier.Should().Be("starter");
        result.Value.Allowances.Single(item => item.Scope == "free" && item.Code == "mockups").Limit.Should().Be(4);
        result.Value.Allowances.Single(item => item.Scope == "trial" && item.Code == "listings").Limit.Should().Be(30);
        settingRows.Single().Value.Should().Be("starter");
        auditLogs.Single().ActionType.Should().Be("UpdatePlanSettings");
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("old", "trial", "products", 10, DisplayName = "a plan not on sale")]
    [DataRow("pro", "free", "mockups", -1, DisplayName = "a negative allowance")]
    [DataRow("pro", "free", "storage", 5, DisplayName = "an unknown allowance")]
    [DataRow("pro", "trial", "products_per_run", 0, DisplayName = "a trial running no product")]
    [DataRow("pro", "trial", "products_per_run", 11, DisplayName = "more per run than the trial's products")]
    [DataRow("starter", "trial", "products_per_run", 25, DisplayName = "more per run than the trial plan allows")]
    public async Task UpdateAsync_RefusesSettingsThatCannotWork(string tier, string scope, string code, int limit)
    {
        var service = CreateService();
        if (tier == "starter")
            await service.UpdateAsync(Request("pro", new AllowanceValueDto("trial", "products", 30)));
        unitOfWork.Invocations.Clear();

        var result = await service.UpdateAsync(Request(tier, new AllowanceValueDto(scope, code, limit)));

        result.IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_SavesTheTrialLengthForNewTrials()
    {
        var service = CreateService();

        var result = await service.UpdateAsync(new UpdatePlanSettingsRequestDto("pro", [], TrialDays: 30));

        result.Value.TrialDays.Should().Be(30);
        settingRows.Single(row => row.Key == PlanSettingKeys.TrialDays).Value.Should().Be("30");
        (await service.UpdateAsync(Request("pro"))).Value.TrialDays.Should().Be(30, "leaving it out keeps the length");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(91)]
    public async Task UpdateAsync_RefusesATrialLengthOutOfRange(int days)
    {
        var result = await CreateService().UpdateAsync(new UpdatePlanSettingsRequestDto("pro", [], days));

        result.IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SetRecommendedPlanAsync_RecommendsProUntilChangedThenNone()
    {
        var service = CreateService();
        (await service.GetAsync()).Value.RecommendedPlanTier.Should().Be("pro");

        var starter = await service.SetRecommendedPlanAsync(new SetRecommendedPlanRequestDto("starter"));
        starter.Value.RecommendedPlanTier.Should().Be("starter");

        var none = await service.SetRecommendedPlanAsync(new SetRecommendedPlanRequestDto(null));
        none.Value.RecommendedPlanTier.Should().BeNull();
        settingRows.Single(row => row.Key == PlanSettingKeys.RecommendedPlanTier).Value.Should().BeEmpty();
        auditLogs.Should().HaveCount(2).And.OnlyContain(log => log.ActionType == "SetRecommendedPlan");
    }

    [TestMethod]
    [DataRow("old", DisplayName = "a plan not on sale")]
    [DataRow("missing", DisplayName = "an unknown plan")]
    public async Task SetRecommendedPlanAsync_RefusesAPlanNotOnSale(string tier)
    {
        var result = await CreateService().SetRecommendedPlanAsync(new SetRecommendedPlanRequestDto(tier));

        result.IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
