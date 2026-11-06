using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Entitlements;

[TestClass]
public sealed class FeatureCatalogTests
{
    private static Feature Row(string code, string label, int sortOrder, string kind = PlanFeatureKinds.Flag) => new()
    {
        Code = code,
        Label = label,
        Description = "",
        GroupName = "Video",
        Kind = kind,
        TrialAvailable = true,
        IsActive = true,
        SortOrder = sortOrder
    };

    private static (FeatureCatalog Catalog, Mock<IRepository<Feature>> Repository) Create(List<Feature> rows, FeatureCatalogCache? cache = null)
    {
        var repository = new Mock<IRepository<Feature>>();
        repository.Setup(candidate => candidate.Query()).Returns(() => rows.AsQueryable().BuildMock());
        var catalog = new FeatureCatalog(
            repository.Object,
            cache ?? new FeatureCatalogCache(SubscriptionTestData.CreateTimeProvider()),
            NullLogger<FeatureCatalog>.Instance);
        return (catalog, repository);
    }

    [TestMethod]
    public async Task GetAllAsync_UsesTheRowsAndFallsBackToCodeForMissingOnes()
    {
        var (catalog, _) = Create([Row(PlanFeatures.AiVideo, "Veo videos", 1)]);

        var entries = await catalog.GetAllAsync();

        entries.Should().HaveCount(PlanFeatures.All.Count);
        entries[0].Should().Match<FeatureCatalogEntry>(entry => entry.Code == PlanFeatures.AiVideo && entry.Label == "Veo videos");
        entries.Single(entry => entry.Code == PlanFeatures.StudioKeyframe).TrialAvailable
            .Should().BeFalse("a code with no row keeps the trial exclusion defined in code");
    }

    [TestMethod]
    public async Task GetAllAsync_KeepsTheKindFromCodeAndIgnoresUnknownCodes()
    {
        var (catalog, _) = Create(
        [
            Row(PlanFeatures.DesignVariations, "Variations", 10, kind: PlanFeatureKinds.Flag),
            Row("video_generation", "Old feature", 20)
        ]);

        var entries = await catalog.GetAllAsync();

        entries.Single(entry => entry.Code == PlanFeatures.DesignVariations).Kind.Should().Be(PlanFeatureKinds.Limit);
        entries.Should().NotContain(entry => entry.Code == "video_generation");
    }

    [TestMethod]
    public async Task GetAllAsync_ReadsTheTableOnceUntilInvalidated()
    {
        var cache = new FeatureCatalogCache(SubscriptionTestData.CreateTimeProvider());
        var (catalog, repository) = Create([Row(PlanFeatures.AiVideo, "Veo videos", 1)], cache);

        await catalog.GetAllAsync();
        await catalog.GetAllAsync();
        catalog.Invalidate();
        await catalog.GetAllAsync();

        repository.Verify(candidate => candidate.Query(), Times.Exactly(2));
    }

    [TestMethod]
    public async Task GetAllAsync_WhenTheTableCannotBeRead_ReturnsTheDefinitionsInCode()
    {
        var repository = new Mock<IRepository<Feature>>();
        repository.Setup(candidate => candidate.Query()).Throws(new InvalidOperationException("relation \"features\" does not exist"));
        var catalog = new FeatureCatalog(repository.Object, new FeatureCatalogCache(TimeProvider.System), NullLogger<FeatureCatalog>.Instance);

        var entries = await catalog.GetAllAsync();

        entries.Should().BeEquivalentTo(FeatureCatalogEntry.Defaults);
    }
}
