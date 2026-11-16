using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Application.Features.Admin.Moderation;
using APCS.Application.Features.Admin.Moderation.Dtos;
using APCS.Application.Features.Admin.Moderation.Validators;
using APCS.Application.Features.Moderation;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Admin.Moderation;

[TestClass]
public sealed class AdminModerationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid SellerA = Guid.NewGuid();
    private static readonly Guid SellerB = Guid.NewGuid();

    private readonly List<ModerationCheck> checkRows = [];
    private readonly List<ModerationTerm> termRows = [];
    private readonly List<ModerationAllowlist> allowRows = [];
    private readonly List<ModerationSetting> settingRows = [];
    private readonly List<AuditLog> audits = [];
    private readonly Mock<IModerationStore> store = new();
    private readonly Mock<IModerationCheckRepository> search = new();
    private readonly Mock<IUnitOfWork> unitOfWork = new();
    private bool storageReady = true;

    [TestInitialize]
    public void SetUp()
    {
        store.Setup(x => x.IsStorageReadyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => storageReady);
        store.Setup(x => x.LoadListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ModerationLists.Empty);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [TestMethod]
    public async Task GetOverviewAsync_TablesMissing_SaysSoWithAnEmptyRange()
    {
        storageReady = false;

        var overview = (await Service().GetOverviewAsync(7)).Value;

        overview.StorageReady.Should().BeFalse();
        overview.Daily.Should().HaveCount(7).And.OnlyContain(day => day.Blocked == 0);
    }

    [TestMethod]
    public async Task GetOverviewAsync_CountsDecisionsPerDayAndBreaksThemDown()
    {
        Check(SellerA, "block", "blocklist", 0, """[{"category":"sexual","severity":"high"}]""", latency: 10);
        Check(SellerA, "block", "gemini", 1, """[{"category":"hate","severity":"high"},{"category":"sexual","severity":"medium"}]""", latency: 1200);
        Check(SellerB, "flagged", "gemini", 1, """[{"category":"violence_gore","severity":"medium"}]""", latency: 900);
        Check(SellerB, "allow", "gemini", 2, "[]", feature: ModerationFeatures.AiVideo, latency: 800);
        Check(SellerB, "block", "blocklist", 40, """[{"category":"sexual","severity":"high"}]""");

        var overview = (await Service().GetOverviewAsync(30)).Value;

        overview.StorageReady.Should().BeTrue();
        overview.Totals.Should().Be(new ModerationTotalsDto(4, 1, 1, 2, 0, 0.5, 728));
        overview.Daily.Should().HaveCount(30);
        overview.Daily[^1].Blocked.Should().Be(1, "today");
        overview.Daily[^2].Should().Be(new ModerationDailyPointDto(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1), 0, 1, 1, 0));
        overview.ByCategory.Should().ContainEquivalentOf(new ModerationCategoryCountDto("sexual", 2, 0));
        overview.ByCategory.Should().ContainEquivalentOf(new ModerationCategoryCountDto("violence_gore", 0, 1));
        overview.ByDecider.Should().BeEquivalentTo([new ModerationCountDto("blocklist", 1), new ModerationCountDto("gemini", 1)]);
        overview.ByFeature.Should().ContainEquivalentOf(new ModerationFeatureCountDto(ModerationFeatures.DesignGeneration, 3, 2));
    }

    [TestMethod]
    public async Task GetTopOffendersAsync_RanksSellersByBlocksWithTheirMostCommonCategory()
    {
        Check(SellerA, "block", "gemini", 0, """[{"category":"hate","severity":"high"}]""");
        Check(SellerA, "block", "gemini", 1, """[{"category":"hate","severity":"high"}]""");
        Check(SellerA, "block", "blocklist", 2, """[{"category":"sexual","severity":"high"}]""");
        Check(SellerB, "block", "gemini", 0, """[{"category":"sexual","severity":"high"}]""");
        Check(SellerB, "flagged", "gemini", 0, """[{"category":"sexual","severity":"medium"}]""");

        var offenders = (await Service().GetTopOffendersAsync(30, 10)).Value;

        offenders.Select(offender => offender.UserId).Should().Equal(SellerA, SellerB);
        offenders[0].Should().BeEquivalentTo(new { Email = "a@example.com", Blocked = 3, Flagged = 0, TopCategory = "hate" });
        offenders[1].Should().BeEquivalentTo(new { Blocked = 1, Flagged = 1, TopCategory = "sexual" });
    }

    [TestMethod]
    public async Task ListChecksAsync_ShortensTheExcerptAndReadsTheCategories()
    {
        search.Setup(x => x.SearchAsync(It.IsAny<ModerationCheckSearch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ModerationCheckRow>(
                [new ModerationCheckRow(Guid.NewGuid(), Now.UtcDateTime, SellerA, "a@example.com", "A", ModerationFeatures.DesignGeneration,
                    "input", "product", "block", "gemini", """[{"category":"sexual","severity":"high"},{"category":"sexual","severity":"low"}]""",
                    new string('x', 400), 900)], 1, 1, 20));

        var page = (await Service().ListChecksAsync(new ListModerationChecksRequestDto(Category: " sexual ", Search: "a@"))).Value;

        page.Items.Should().ContainSingle();
        page.Items[0].Categories.Should().Equal("sexual");
        page.Items[0].ExcerptPreview.Should().HaveLength(160).And.EndWith("…");
        search.Verify(x => x.SearchAsync(It.Is<ModerationCheckSearch>(filter => filter.Category == "sexual" && filter.Seller == "a@"),
            It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task CreateTermAsync_NormalizesRecordsAndRefreshesTheBlocklist()
    {
        var blocklist = Blocklist();
        await blocklist.GetAsync(default);

        var result = await Service(blocklist).CreateTermAsync(
            new SaveModerationTermRequestDto("  Bú   CU ", "sexual", "block", MatchType: "word", Language: "vi"));

        result.Value.Term.Should().Be("bú cu");
        result.Value.MatchType.Should().Be("phrase", "a term of several words can only match as a phrase");
        termRows.Should().ContainSingle(term => term.UpdatedBy == AdminId);
        audits.Should().ContainSingle(audit => audit.ActionType == "CreateModerationTerm" && audit.ActorUserId == AdminId);
        await blocklist.GetAsync(default);
        store.Verify(x => x.LoadListsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task CreateTermAsync_ExistingTerm_IsAConflict()
    {
        termRows.Add(new ModerationTerm { Id = Guid.NewGuid(), Term = "nứng", Category = "sexual", Severity = "block", MatchType = "word", Language = "vi" });

        var result = await Service().CreateTermAsync(new SaveModerationTermRequestDto("NỨNG", "sexual", "block"));

        result.Error.Code.Should().Be("ModerationTermDuplicate");
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateTermAsync_UnknownCategory_IsRejected()
    {
        (await Service().CreateTermAsync(new SaveModerationTermRequestDto("x", "weather", "block"))).IsFailure.Should().BeTrue();
        termRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CreateTermAsync_TablesMissing_IsRefused()
    {
        storageReady = false;

        (await Service().CreateTermAsync(new SaveModerationTermRequestDto("x", "sexual", "block"))).Error.Code
            .Should().Be("ModerationStorageMissing");
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_ChangesTheThresholdAndAppliesItAtOnce()
    {
        settingRows.Add(new ModerationSetting { Key = "enabled", Value = "true" });
        settingRows.Add(new ModerationSetting { Key = "block_at.sexual", Value = "high" });
        var runtime = Runtime();
        await runtime.GetAsync(default);

        var result = await Service(runtime: runtime).UpdateSettingsAsync(new UpdateModerationSettingsRequestDto(true,
            new Dictionary<string, string> { ["sexual"] = "medium" }));

        settingRows.Single(row => row.Key == "block_at.sexual").Should().BeEquivalentTo(new { Value = "medium", UpdatedBy = AdminId });
        result.Value.Thresholds.Should().ContainEquivalentOf(new ModerationThresholdDto("sexual", "medium", false));
        audits.Should().ContainSingle(audit => audit.ActionType == "UpdateModerationSettings");
        await runtime.GetAsync(default);
        store.Verify(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_MinorsThreshold_CannotBeChanged()
    {
        var result = await Service().UpdateSettingsAsync(new UpdateModerationSettingsRequestDto(true,
            new Dictionary<string, string> { ["sexual_minors"] = "high" }));

        result.IsFailure.Should().BeTrue();
        settingRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetSettingsAsync_ShowsEveryCategoryWithMinorsLocked()
    {
        settingRows.Add(new ModerationSetting { Key = "enabled", Value = "false", UpdatedAt = Now.UtcDateTime, UpdatedBy = SellerA });

        var settings = (await Service().GetSettingsAsync()).Value;

        settings.Enabled.Should().BeFalse();
        settings.UpdatedByEmail.Should().Be("a@example.com");
        settings.Thresholds.Should().HaveCount(ModerationCategories.All.Count);
        settings.Thresholds.Should().ContainEquivalentOf(new ModerationThresholdDto("sexual_minors", "any", true));
        settings.Thresholds.Where(threshold => !threshold.Locked).Should().OnlyContain(threshold => threshold.BlockAt == "high");
    }

    private void Check(Guid userId, string decision, string decider, int daysAgo, string categories,
        string feature = ModerationFeatures.DesignGeneration, int? latency = null) =>
        checkRows.Add(new ModerationCheck
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Feature = feature,
            Direction = "input",
            SubjectType = "product",
            ContentHash = new string('0', 64),
            Decision = decision,
            DecidedBy = decider,
            Categories = categories,
            LatencyMs = latency,
            PolicyVersion = 1,
            CreatedAt = Now.UtcDateTime.AddDays(-daysAgo)
        });

    private ModerationBlocklistCache Blocklist() =>
        new(store.Object, Options.Create(new ModerationOptions()), new FakeTimeProvider(Now), NullLogger<ModerationBlocklistCache>.Instance);

    private ModerationRuntimeSettings Runtime() =>
        new(store.Object, Options.Create(new ModerationOptions()), new FakeTimeProvider(Now), NullLogger<ModerationRuntimeSettings>.Instance);

    private AdminModerationService Service(ModerationBlocklistCache? blocklist = null, ModerationRuntimeSettings? runtime = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(AdminId);
        var gemini = new Mock<IGeminiTextGenerationProvider>();
        gemini.SetupGet(x => x.IsConfigured).Returns(true);
        var people = new List<User>
        {
            new() { Id = SellerA, Email = "a@example.com", FullName = "Seller A" },
            new() { Id = SellerB, Email = "b@example.com", FullName = "Seller B" }
        };
        var settingsRepo = Repo(settingRows);
        settingsRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object key, CancellationToken _) => settingRows.SingleOrDefault(row => row.Key == (string)key));
        var termRepo = Repo(termRows);
        termRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => termRows.SingleOrDefault(row => row.Id == (Guid)id));

        return new AdminModerationService(Repo(checkRows).Object, termRepo.Object, Repo(allowRows).Object, settingsRepo.Object,
            Repo(people).Object, Repo(audits).Object, search.Object, store.Object, blocklist ?? Blocklist(), runtime ?? Runtime(),
            gemini.Object, Options.Create(new ModerationOptions()), currentUser.Object, unitOfWork.Object, new FakeTimeProvider(Now),
            new SaveModerationTermValidator(), new SaveModerationAllowlistValidator(), new UpdateModerationSettingsValidator());
    }

    // Query() reads the live list, so rows added by the service are seen by a later query.
    private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> rows) where TEntity : class
    {
        var repo = new Mock<IRepository<TEntity>>();
        repo.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        repo.Setup(x => x.AddAsync(It.IsAny<TEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<TEntity, bool, CancellationToken>((entity, _, _) => rows.Add(entity))
            .Returns(Task.CompletedTask);
        repo.Setup(x => x.UpdateAsync(It.IsAny<TEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(x => x.RemoveAsync(It.IsAny<TEntity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<TEntity, bool, CancellationToken>((entity, _, _) => rows.Remove(entity))
            .Returns(Task.CompletedTask);
        return repo;
    }
}
