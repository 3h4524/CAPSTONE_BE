using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanFeatureAssignmentServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime);

    private readonly SubscriptionPlan plan = new() { Id = Guid.NewGuid(), Name = "Pro", Tier = "pro", MonthlyPriceUsd = 29.99m };
    private readonly List<PlanFeature> rows = [];
    private readonly List<Subscription> subscriptions = [];
    private readonly List<SubscriptionFeatureGrant> grants = [];
    private readonly List<AuditLog> auditLogs = [];
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private IReadOnlySet<Guid>? usersOfFeature = new HashSet<Guid>();

    private Subscription Subscribe(DateOnly renewal, DateTime? trialEndsAt = null, string status = "active", Guid? planId = null)
    {
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PlanId = planId ?? plan.Id,
            BillingCycle = "monthly",
            Status = status,
            RenewalDate = renewal,
            TrialEndsAt = trialEndsAt,
        };
        subscriptions.Add(subscription);
        return subscription;
    }

    private void Has(string code, bool isEnabled = true, int? limit = null) =>
        rows.Add(new PlanFeature { PlanId = plan.Id, FeatureCode = code, IsEnabled = isEnabled, LimitValue = limit });

    private PlanFeatureAssignmentService CreateService()
    {
        Mock<IRepository<T>> Repository<T>(List<T> items) where T : class
        {
            var repository = new Mock<IRepository<T>>();
            repository.Setup(candidate => candidate.Query()).Returns(() => items.AsQueryable().BuildMock());
            repository.Setup(candidate => candidate.AddAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<T, bool, CancellationToken>((item, _, _) => items.Add(item)).Returns(Task.CompletedTask);
            repository.Setup(candidate => candidate.RemoveAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<T, bool, CancellationToken>((item, _, _) => items.Remove(item)).Returns(Task.CompletedTask);
            return repository;
        }

        var insights = new Mock<IPlanInsightsService>();
        insights.Setup(candidate => candidate.UsersOfFeatureAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => usersOfFeature);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.UserId).Returns(Guid.NewGuid());

        return new PlanFeatureAssignmentService(
            Repository([plan]).Object,
            Repository(rows).Object,
            Repository(subscriptions).Object,
            Repository(grants).Object,
            Repository(auditLogs).Object,
            new TestFeatureCatalog(),
            insights.Object,
            unitOfWork.Object,
            currentUser.Object,
            SubscriptionTestData.CreateTimeProvider());
    }

    [TestMethod]
    public async Task RemoveAsync_LetsEveryoneOnThePlanKeepItUntilTheirPeriodEnds()
    {
        Has(PlanFeatures.LifestyleMockup);
        var monthly = Subscribe(Today.AddDays(12));
        var annual = Subscribe(Today.AddDays(300));
        var trial = Subscribe(Today.AddDays(14), trialEndsAt: SubscriptionTestData.UtcNow.UtcDateTime.AddDays(5));
        Subscribe(Today, status: "past_due");
        Subscribe(Today.AddDays(20), planId: Guid.NewGuid());

        var result = await CreateService().RemoveAsync(plan.Id, PlanFeatures.LifestyleMockup);

        result.IsSuccess.Should().BeTrue();
        rows.Single().IsEnabled.Should().BeFalse("new subscribers no longer get it");
        grants.Should().HaveCount(3, "a period already over and another plan's subscriber keep nothing");
        grants.Single(grant => grant.SubscriptionId == monthly.Id).ExpiresOn.Should().Be(Today.AddDays(12));
        grants.Single(grant => grant.SubscriptionId == annual.Id).ExpiresOn.Should().Be(Today.AddDays(300));
        grants.Single(grant => grant.SubscriptionId == trial.Id).ExpiresOn.Should().Be(Today.AddDays(6), "a trial keeps it to the day after it ends");
        grants.Should().OnlyContain(grant => grant.IsEnabled && grant.Reason == EntitlementMerge.FeatureRemoved && grant.PlanId == plan.Id);

        var change = result.Value.Changes.Single();
        change.Before.IsEnabled.Should().BeTrue();
        change.After.IsEnabled.Should().BeFalse();
        change.KeptBy.Should().Be(3);
        change.KeptUntil.Should().Be(Today.AddDays(300));
        auditLogs.Single().ActionType.Should().Be("ChangePlanFeature");
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private void GivenAgo(string code, TimeSpan ago) =>
        auditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActionType = "ChangePlanFeature",
            ResourceType = "SubscriptionPlan",
            ResourceId = plan.Id,
            OldValue = $$"""{"code": "{{code}}", "isEnabled": false, "limitValue": null}""",
            NewValue = $$"""{"code": "{{code}}", "isEnabled": true, "limitValue": null, "keptBy": 0}""",
            CreatedAt = SubscriptionTestData.UtcNow.UtcDateTime - ago,
        });

    [TestMethod]
    public async Task RemoveAsync_AFeatureGivenMidPeriod_KeepsItForTheNoticeDaysOnly()
    {
        // Today is 11 Sep 2026; Lifestyle mockups was added on 1 Sep.
        Has(PlanFeatures.LifestyleMockup);
        GivenAgo(PlanFeatures.LifestyleMockup, TimeSpan.FromDays(10));
        var paidFor = Subscribe(Today.AddDays(25));
        var monthly = Subscribe(Today.AddDays(12));
        var annual = Subscribe(Today.AddDays(300));
        annual.BillingCycle = "annual";
        var service = CreateService();

        var impact = (await service.GetImpactAsync(plan.Id, PlanFeatures.LifestyleMockup, null)).Value;
        await service.RemoveAsync(plan.Id, PlanFeatures.LifestyleMockup);

        grants.Single(grant => grant.SubscriptionId == paidFor.Id).ExpiresOn
            .Should().Be(Today.AddDays(25), "its period began on 6 Sep, with the feature already on the plan");
        grants.Single(grant => grant.SubscriptionId == monthly.Id).ExpiresOn
            .Should().Be(Today.AddDays(12), "given mid-period, but the period ends before the 30 days");
        grants.Single(grant => grant.SubscriptionId == annual.Id).ExpiresOn
            .Should().Be(Today.AddDays(PlanFeatureAssignmentService.NoticeDays), "given mid-year: 30 days' notice, not the rest of the year");
        impact.PaidForBy.Should().Be(1);
        impact.NoticeBy.Should().Be(2);
        impact.NoticeUntil.Should().Be(Today.AddDays(30));
        impact.GivenRecently.Should().BeFalse();
    }

    [TestMethod]
    public async Task RemoveAsync_AFeatureAddedLessThanADayAgo_KeepsNothing()
    {
        Has(PlanFeatures.LifestyleMockup);
        GivenAgo(PlanFeatures.LifestyleMockup, TimeSpan.FromHours(2));
        Subscribe(Today.AddDays(300));
        var service = CreateService();

        var impact = (await service.GetImpactAsync(plan.Id, PlanFeatures.LifestyleMockup, null)).Value;
        var result = await service.RemoveAsync(plan.Id, PlanFeatures.LifestyleMockup);

        impact.GivenRecently.Should().BeTrue();
        impact.KeptBy.Should().Be(0);
        result.Value.Changes.Single().KeptBy.Should().Be(0);
        grants.Should().BeEmpty("undoing a mistake gives nothing away");
    }

    [TestMethod]
    public async Task RemoveAsync_ATrialKeepsNothingTrialsLeaveOut()
    {
        Has(PlanFeatures.AiVideo);
        Subscribe(Today.AddDays(14), trialEndsAt: SubscriptionTestData.UtcNow.UtcDateTime.AddDays(5));

        await CreateService().RemoveAsync(plan.Id, PlanFeatures.AiVideo);

        grants.Should().BeEmpty("the trial never had AI video");
    }

    [TestMethod]
    public async Task RemoveAsync_OnAFeatureThePlanDoesNotHave_KeepsNothing()
    {
        Subscribe(Today.AddDays(12));

        var result = await CreateService().RemoveAsync(plan.Id, PlanFeatures.AiVideo);

        result.Value.Changes.Single().KeptBy.Should().Be(0);
        grants.Should().BeEmpty();
    }

    [TestMethod]
    public async Task RemoveAsync_OnALimit_IsRefused()
    {
        var result = await CreateService().RemoveAsync(plan.Id, PlanFeatures.DesignVariations);

        result.Error.Code.Should().Be(ErrorCodes.PlanFeatureInvalidChange);
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AddAsync_PuttingAFeatureBack_DropsWhatSubscribersWereKeeping()
    {
        Has(PlanFeatures.LifestyleMockup);
        Subscribe(Today.AddDays(12));
        var service = CreateService();
        await service.RemoveAsync(plan.Id, PlanFeatures.LifestyleMockup);

        var result = await service.AddAsync(plan.Id, new AddPlanFeaturesRequestDto([new AddPlanFeatureDto(PlanFeatures.LifestyleMockup)]));

        result.Value.Changes.Single().After.IsEnabled.Should().BeTrue();
        rows.Single().IsEnabled.Should().BeTrue();
        grants.Should().BeEmpty("the plan gives it again");
    }

    [TestMethod]
    public async Task AddAsync_AddsNewRowsAndRefusesUnknownOrRepeatedCodes()
    {
        var service = CreateService();

        var added = await service.AddAsync(plan.Id, new AddPlanFeaturesRequestDto(
            [new AddPlanFeatureDto(PlanFeatures.AiVideo), new AddPlanFeatureDto(PlanFeatures.DailyVideoCap, 40)]));
        var unknown = await service.AddAsync(plan.Id, new AddPlanFeaturesRequestDto([new AddPlanFeatureDto("video_generation")]));
        var repeated = await service.AddAsync(plan.Id, new AddPlanFeaturesRequestDto(
            [new AddPlanFeatureDto(PlanFeatures.AiVideo), new AddPlanFeatureDto(PlanFeatures.AiVideo)]));

        added.Value.Changes.Should().HaveCount(2);
        rows.Should().ContainSingle(row => row.FeatureCode == PlanFeatures.DailyVideoCap && row.LimitValue == 40);
        unknown.Error.Code.Should().Be(ErrorCodes.PlanFeatureNotFound);
        repeated.Error.Code.Should().Be(ErrorCodes.PlanFeatureInvalidChange);
    }

    [TestMethod]
    public async Task UpdateLimitAsync_Lowering_KeepsTheOldValueAndRaising_KeepsNothing()
    {
        Has(PlanFeatures.DesignVariations, limit: 4);
        var subscription = Subscribe(Today.AddDays(12));
        var service = CreateService();

        await service.UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 2);

        grants.Single().Should().Match<SubscriptionFeatureGrant>(grant =>
            grant.SubscriptionId == subscription.Id && grant.LimitValue == 4 && grant.Reason == EntitlementMerge.LimitLowered);

        await service.UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 6);

        rows.Single().LimitValue.Should().Be(6);
        grants.Should().BeEmpty("the plan now goes further than what was kept");
    }

    [TestMethod]
    public async Task UpdateLimitAsync_FromNoLimit_KeepsNoLimit()
    {
        Subscribe(Today.AddDays(12));

        await CreateService().UpdateLimitAsync(plan.Id, PlanFeatures.DailyMockupCap, 300);

        grants.Single().LimitValue.Should().BeNull("there was no cap before");
    }

    [TestMethod]
    public async Task UpdateLimitAsync_OutOfRangeOrOnAFlag_IsRefused()
    {
        var service = CreateService();

        (await service.UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 10_001)).IsFailure.Should().BeTrue();
        (await service.UpdateLimitAsync(plan.Id, PlanFeatures.AiVideo, 3)).IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetImpactAsync_CountsWhoKeepsItAndWhoUsedIt()
    {
        Has(PlanFeatures.LifestyleMockup);
        var user = Subscribe(Today.AddDays(12));
        Subscribe(Today.AddDays(40));
        Subscribe(Today.AddDays(14), trialEndsAt: SubscriptionTestData.UtcNow.UtcDateTime.AddDays(5));
        usersOfFeature = new HashSet<Guid> { user.UserId, Guid.NewGuid() };

        var impact = (await CreateService().GetImpactAsync(plan.Id, PlanFeatures.LifestyleMockup, null)).Value;

        impact.Subscribers.Should().Be(2);
        impact.Trials.Should().Be(1);
        impact.IsReduction.Should().BeTrue();
        impact.KeptBy.Should().Be(3);
        impact.KeptUntil.Should().Be(Today.AddDays(40));
        impact.UsedRecently.Should().Be(1, "only users on this plan count");
        grants.Should().BeEmpty("a preview changes nothing");
    }

    [TestMethod]
    public async Task GetImpactAsync_RaisingALimit_IsNotAReduction()
    {
        Has(PlanFeatures.DesignVariations, limit: 2);
        Subscribe(Today.AddDays(12));
        usersOfFeature = null;

        var impact = (await CreateService().GetImpactAsync(plan.Id, PlanFeatures.DesignVariations, -1)).Value;

        impact.IsReduction.Should().BeFalse();
        impact.KeptBy.Should().Be(0);
        impact.Proposed.LimitValue.Should().BeNull();
        impact.UsedRecently.Should().BeNull("not every feature is measured");
    }

    private void ChangedAgo(string code, TimeSpan ago, bool beforeEnabled, int? beforeLimit, bool afterEnabled, int? afterLimit) =>
        auditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActionType = "ChangePlanFeature",
            ResourceType = "SubscriptionPlan",
            ResourceId = plan.Id,
            OldValue = JsonSerializer.Serialize(new { code, isEnabled = beforeEnabled, limitValue = beforeLimit }),
            NewValue = JsonSerializer.Serialize(new { code, isEnabled = afterEnabled, limitValue = afterLimit, keptBy = 0 }),
            CreatedAt = SubscriptionTestData.UtcNow.UtcDateTime - ago,
        });

    [TestMethod]
    public async Task UpdateLimitAsync_BelowWhatARecentRaiseStartedFrom_KeepsTheValueBeforeTheRaise()
    {
        // Subscribers had 10. An Admin raised it to 20 an hour ago and now sets it to 5: the raise
        // gives nobody anything, but the 10 they paid for is still theirs.
        Has(PlanFeatures.DesignVariations, limit: 20);
        ChangedAgo(PlanFeatures.DesignVariations, TimeSpan.FromHours(1), true, 10, true, 20);
        var subscription = Subscribe(Today.AddDays(12));
        var service = CreateService();

        var impact = (await service.GetImpactAsync(plan.Id, PlanFeatures.DesignVariations, 5)).Value;
        await service.UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 5);

        impact.GivenRecently.Should().BeFalse();
        impact.KeptBy.Should().Be(1);
        rows.Single().LimitValue.Should().Be(5);
        grants.Single().Should().Match<SubscriptionFeatureGrant>(grant =>
            grant.SubscriptionId == subscription.Id && grant.LimitValue == 10);
    }

    [TestMethod]
    public async Task UpdateLimitAsync_UndoingARecentRaise_KeepsNothing()
    {
        Has(PlanFeatures.DesignVariations, limit: 20);
        ChangedAgo(PlanFeatures.DesignVariations, TimeSpan.FromHours(1), true, 10, true, 20);
        Subscribe(Today.AddDays(12));
        var service = CreateService();

        var impact = (await service.GetImpactAsync(plan.Id, PlanFeatures.DesignVariations, 10)).Value;
        await service.UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 10);

        impact.GivenRecently.Should().BeTrue();
        grants.Should().BeEmpty("nobody loses anything they had before the raise");
    }

    [TestMethod]
    public async Task RemoveAsync_AgainAfterUndoingARemoval_StillKeepsWhatWasPaidFor()
    {
        // A long-standing feature was removed, the Admin undid it, and now removes it again.
        Has(PlanFeatures.LifestyleMockup);
        ChangedAgo(PlanFeatures.LifestyleMockup, TimeSpan.FromHours(3), true, null, false, null);
        ChangedAgo(PlanFeatures.LifestyleMockup, TimeSpan.FromHours(1), false, null, true, null);
        var subscription = Subscribe(Today.AddDays(12));

        await CreateService().RemoveAsync(plan.Id, PlanFeatures.LifestyleMockup);

        grants.Single().Should().Match<SubscriptionFeatureGrant>(grant =>
            grant.SubscriptionId == subscription.Id && grant.IsEnabled == true && grant.ExpiresOn == subscription.RenewalDate);
    }

    [TestMethod]
    public async Task UpdateLimitAsync_AfterAnOldRaiseOutsideTheWindow_DatesThePaidPeriodFromIt()
    {
        // The raise is settled, so a subscriber whose period began before it only keeps the notice days.
        Has(PlanFeatures.DesignVariations, limit: 20);
        ChangedAgo(PlanFeatures.DesignVariations, TimeSpan.FromDays(3), true, 10, true, 20);
        Subscribe(Today.AddDays(300)).BillingCycle = "annual";

        await CreateService().UpdateLimitAsync(plan.Id, PlanFeatures.DesignVariations, 5);

        grants.Single().Should().Match<SubscriptionFeatureGrant>(grant =>
            grant.LimitValue == 20 && grant.ExpiresOn == Today.AddDays(PlanFeatureAssignmentService.NoticeDays));
    }
}
