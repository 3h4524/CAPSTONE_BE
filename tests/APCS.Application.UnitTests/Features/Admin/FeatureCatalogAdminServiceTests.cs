using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Validators;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class FeatureCatalogAdminServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly List<Feature> rows = [];
    private readonly List<Feature> updated = [];
    private readonly List<AuditLog> auditLogs = [];
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private readonly TestFeatureCatalog catalog = new();

    private static UpdateFeatureRequestDto Request(string label = "Veo videos", int? defaultLimit = null) =>
        new(label, " Videos from AI scenes. ", "Video", "per day", defaultLimit, TrialAvailable: true, IsActive: false, SortOrder: 15);

    private FeatureCatalogAdminService CreateService()
    {
        var starter = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Starter", Tier = "starter", MonthlyPriceUsd = 9.99m };
        var pro = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = "pro", MonthlyPriceUsd = 29.99m };
        var planFeatures = new List<PlanFeature>
        {
            new() { Plan = pro, PlanId = pro.Id, FeatureCode = PlanFeatures.AiVideo, IsEnabled = true },
            new() { Plan = starter, PlanId = starter.Id, FeatureCode = PlanFeatures.AiVideo, IsEnabled = false },
            new() { Plan = pro, PlanId = pro.Id, FeatureCode = PlanFeatures.AutoApproval, IsEnabled = true },
            new() { Plan = starter, PlanId = starter.Id, FeatureCode = PlanFeatures.AutoApproval, IsEnabled = true },
        };

        var features = new Mock<IRepository<Feature>>();
        features.Setup(candidate => candidate.Query()).Returns(() => rows.AsQueryable().BuildMock());
        features.Setup(candidate => candidate.AddAsync(It.IsAny<Feature>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Feature, bool, CancellationToken>((row, _, _) => rows.Add(row))
            .Returns(Task.CompletedTask);
        features.Setup(candidate => candidate.UpdateAsync(It.IsAny<Feature>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Feature, bool, CancellationToken>((row, _, _) => updated.Add(row))
            .Returns(Task.CompletedTask);
        var planFeatureRepository = new Mock<IRepository<PlanFeature>>();
        planFeatureRepository.Setup(candidate => candidate.Query()).Returns(() => planFeatures.AsQueryable().BuildMock());
        var users = new Mock<IRepository<User>>();
        users.Setup(candidate => candidate.Query()).Returns(() => new List<User> { new() { Id = AdminId, FullName = "Ada Admin", Email = "a@x", AccountStatus = "active" } }.AsQueryable().BuildMock());
        var audit = new Mock<IRepository<AuditLog>>();
        audit.Setup(candidate => candidate.AddAsync(It.IsAny<AuditLog>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, bool, CancellationToken>((log, _, _) => auditLogs.Add(log))
            .Returns(Task.CompletedTask);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.UserId).Returns(AdminId);

        return new FeatureCatalogAdminService(
            catalog,
            features.Object,
            planFeatureRepository.Object,
            users.Object,
            audit.Object,
            unitOfWork.Object,
            currentUser.Object,
            SubscriptionTestData.CreateTimeProvider(),
            new UpdateFeatureRequestDtoValidator());
    }

    [TestMethod]
    public async Task GetAllAsync_ListsThePlansThatIncludeEachFeature()
    {
        var result = await CreateService().GetAllAsync();

        result.Value.Single(feature => feature.Code == PlanFeatures.AiVideo).Plans.Should().Equal("Pro");
        result.Value.Single(feature => feature.Code == PlanFeatures.AutoApproval).Plans.Should().Equal("Starter", "Pro");
        result.Value.Single(feature => feature.Code == PlanFeatures.WorkflowPublish).Plans.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateAsync_ForAnUnknownCode_ReturnsNotFound()
    {
        var result = await CreateService().UpdateAsync("video_generation", Request());

        result.Error.Code.Should().Be(ErrorCodes.PlanFeatureNotFound);
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WithAnEmptyLabel_FailsValidation()
    {
        var result = await CreateService().UpdateAsync(PlanFeatures.AiVideo, Request(label: " "));

        result.IsFailure.Should().BeTrue();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WritesTheRowRecordsTheChangeAndRefreshesTheCatalog()
    {
        var result = await CreateService().UpdateAsync(PlanFeatures.AiVideo, Request(defaultLimit: 5));

        result.IsSuccess.Should().BeTrue();
        var row = rows.Single();
        row.Label.Should().Be("Veo videos");
        row.Description.Should().Be("Videos from AI scenes.");
        row.Kind.Should().Be(PlanFeatureKinds.Flag);
        row.Unit.Should().BeNull("a flag counts nothing");
        row.DefaultLimit.Should().BeNull("a flag has no limit");
        row.IsActive.Should().BeFalse();
        row.SortOrder.Should().Be(15);
        row.UpdatedBy.Should().Be(AdminId);

        var log = auditLogs.Single();
        log.ActionType.Should().Be("UpdatePlanFeature");
        log.ActorUserId.Should().Be(AdminId);
        log.OldValue.Should().Contain("\"label\":\"AI video (Veo)\"");
        log.NewValue.Should().Contain("\"label\":\"Veo videos\"");

        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        catalog.Invalidations.Should().Be(1);
    }

    [TestMethod]
    public async Task UpdateAsync_OnASeededRow_UpdatesItInsteadOfAddingAnother()
    {
        rows.Add(new Feature { Code = PlanFeatures.AiVideo, Label = "AI video (Veo)", Description = "", GroupName = "Video", Kind = PlanFeatureKinds.Flag, SortOrder = 70 });

        await CreateService().UpdateAsync(PlanFeatures.AiVideo, Request());

        rows.Should().ContainSingle();
        updated.Should().ContainSingle().Which.Label.Should().Be("Veo videos");
    }

    [TestMethod]
    public async Task UpdateAsync_OnALimit_KeepsItsUnitAndDefault()
    {
        await CreateService().UpdateAsync(PlanFeatures.DailyMockupCap, Request(defaultLimit: 250));

        rows.Single().Should().Match<Feature>(row => row.Unit == "per day" && row.DefaultLimit == 250 && row.Kind == PlanFeatureKinds.Limit);
    }
}
