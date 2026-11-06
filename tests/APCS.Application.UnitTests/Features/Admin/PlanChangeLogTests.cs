using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanChangeLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static async Task<AuditLog> LogAsync(Guid? actor, Guid? planId, object? before, object? after)
    {
        var repo = new Mock<IRepository<AuditLog>>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(actor);

        await new PlanChangeLog(repo.Object, currentUser.Object, new FakeTimeProvider(Now))
            .AddAsync(PlanChangeActions.Update, planId, before, after);

        return repo.Invocations.Should().ContainSingle().Subject.Arguments[0].Should().BeOfType<AuditLog>().Subject;
    }

    [TestMethod]
    public async Task AddAsync_RecordsWhoChangedWhichPlanAndWhen()
    {
        var actor = Guid.NewGuid();
        var planId = Guid.NewGuid();

        var row = await LogAsync(actor, planId, null, null);

        row.ActorUserId.Should().Be(actor);
        row.ActionType.Should().Be(PlanChangeActions.Update);
        row.ResourceType.Should().Be("SubscriptionPlan");
        row.ResourceId.Should().Be(planId);
        row.CreatedAt.Should().Be(Now.UtcDateTime);
    }

    [TestMethod]
    public async Task AddAsync_WritesTheValuesInCamelCaseForTheHistoryTab()
    {
        var row = await LogAsync(Guid.NewGuid(), null, new { MonthlyPriceUsd = 10m }, new { MonthlyPriceUsd = 20m });

        JsonDocument.Parse(row.OldValue!).RootElement.GetProperty("monthlyPriceUsd").GetDecimal().Should().Be(10m);
        JsonDocument.Parse(row.NewValue!).RootElement.GetProperty("monthlyPriceUsd").GetDecimal().Should().Be(20m);
    }

    [TestMethod]
    public async Task AddAsync_LeavesAMissingSideEmpty()
    {
        var row = await LogAsync(Guid.NewGuid(), null, null, null);

        row.OldValue.Should().BeNull();
        row.NewValue.Should().BeNull();
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(null, false)]
    public void PlanSnapshot_IsOnSaleOnlyWhenThePlanIsActive(bool? isActive, bool expected)
    {
        var plan = new SubscriptionPlan { Name = "Pro", Tier = "pro", MonthlyPriceUsd = 29, MaxBatchSize = 50, MaxConcurrentJobs = 2, IsActive = isActive };

        var snapshot = PlanSnapshot.Of(plan);

        snapshot.Name.Should().Be("Pro");
        snapshot.MonthlyPriceUsd.Should().Be(29m);
        snapshot.IsActive.Should().Be(expected);
    }
}
