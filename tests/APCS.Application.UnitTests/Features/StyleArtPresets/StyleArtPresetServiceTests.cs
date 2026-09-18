using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.StyleArtPresets;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.StyleArtPresets;

[TestClass]
public sealed class StyleArtPresetServiceTests
{
    [TestMethod]
    public async Task ListActiveAsync_WhenCalled_ReturnsOnlyActiveOrderedByUsageThenName()
    {
        var currentUser = AuthenticatedUser();
        var repository = PresetRepository(
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Watercolor", Description = "Soft", StyleModifiers = "watercolor", Recommendations = "[\"A\"]", IsActive = true, UsageCount = 5 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Vintage", Description = "Retro", StyleModifiers = "vintage", Recommendations = "[]", IsActive = true, UsageCount = 9 },
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Retired", Description = "Old", StyleModifiers = "old", Recommendations = "[]", IsActive = false, UsageCount = 99 });

        var result = await new StyleArtPresetService(currentUser.Object, repository.Object).ListActiveAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Name.Should().Be("Vintage");
        result.Value[1].Name.Should().Be("Watercolor");
        result.Value[1].Recommendations.Should().ContainSingle().Which.Should().Be("A");
    }

    [TestMethod]
    public async Task ListActiveAsync_WhenUnauthenticated_ReturnsFailure()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(false);

        var result = await new StyleArtPresetService(currentUser.Object, new Mock<IRepository<StyleArtPreset>>().Object).ListActiveAsync();

        result.IsSuccess.Should().BeFalse();
    }

    [TestMethod]
    public async Task ListActiveAsync_WhenRecommendationsInvalid_ReturnsEmptyList()
    {
        var repository = PresetRepository(
            new StyleArtPreset { Id = Guid.NewGuid(), Name = "Broken", Description = "Bad data", StyleModifiers = "x", Recommendations = "not-json", IsActive = true });

        var result = await new StyleArtPresetService(AuthenticatedUser().Object, repository.Object).ListActiveAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Recommendations.Should().BeEmpty();
    }

    private static Mock<ICurrentUser> AuthenticatedUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        return currentUser;
    }

    private static Mock<IRepository<StyleArtPreset>> PresetRepository(params StyleArtPreset[] presets)
    {
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.Query()).Returns(presets.AsQueryable().BuildMock());
        return repository;
    }
}
