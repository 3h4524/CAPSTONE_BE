using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Admin.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using System.Text.Json;

namespace APCS.Application.UnitTests.Features.Admin.StyleArtPresets;

[TestClass]
public sealed class AdminStyleArtPresetServiceTests
{
    private const string TranslatedOld =
        "{\"vi\":{\"name\":\"Ten cu\",\"description\":\"Mo ta cu\",\"recommendations\":[\"Ap phich\"]},\"status\":\"ok\",\"attempts\":0}";

    [TestMethod]
    public async Task CreateAsync_SavesTheStyleAsPendingAndWakesTheTranslationWorker()
    {
        var repository = SystemRepository([]);
        StyleArtPreset? captured = null;
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), true, It.IsAny<CancellationToken>()))
            .Callback<StyleArtPreset, bool, CancellationToken>((preset, _, _) => captured = preset)
            .Returns(Task.CompletedTask);
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        var result = await CreateService(repository, queue).CreateAsync(
            new CreateAdminStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"Posters\"]", null));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be(StyleArtPresetTranslations.Pending);
        result.Value.Vi.Should().BeNull();
        var state = JsonDocument.Parse(captured!.Translations!).RootElement;
        state.GetProperty("status").GetString().Should().Be(StyleArtPresetTranslations.Pending);
        state.GetProperty("attempts").GetInt32().Should().Be(0);
        state.TryGetProperty("vi", out _).Should().BeFalse();
        queue.Verify(x => x.Notify(), Times.Once);
    }

    [TestMethod]
    public async Task CreateAsync_WhenTheNameIsTaken_DoesNotWakeTheTranslationWorker()
    {
        var repository = SystemRepository([SystemPreset("Neon", TranslatedOld)]);
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        var result = await CreateService(repository, queue).CreateAsync(
            new CreateAdminStyleArtPresetRequestDto("neon", "Bold glow", "neon glow", "[]", null));

        result.IsSuccess.Should().BeFalse();
        queue.Verify(x => x.Notify(), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTheEnglishTextChanges_DropsTheOldTranslationAndQueuesANewOne()
    {
        var existing = SystemPreset("Old name", TranslatedOld);
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        var result = await CreateService(SystemRepository([existing]), queue).UpdateAsync(
            existing.Id,
            new UpdateAdminStyleArtPresetRequestDto("New name", "Old description", "old", "[\"Posters\"]", null, false, true));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be(StyleArtPresetTranslations.Pending);
        result.Value.Vi.Should().BeNull();
        StyleArtPresetTranslations.ParseViDocument(existing.Translations).Should().BeNull();
        queue.Verify(x => x.Notify(), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenOnlyTheRecommendationsChange_QueuesANewTranslation()
    {
        var existing = SystemPreset("Neon", TranslatedOld);
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        await CreateService(SystemRepository([existing]), queue).UpdateAsync(
            existing.Id,
            new UpdateAdminStyleArtPresetRequestDto("Neon", "Old description", "old", "[\"Posters\",\"Mugs\"]", null, false, true));

        StyleArtPresetTranslations.ParseState(existing.Translations)!.Status.Should().Be(StyleArtPresetTranslations.Pending);
        queue.Verify(x => x.Notify(), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTheEnglishTextIsUnchanged_KeepsTheTranslation()
    {
        var existing = SystemPreset("Neon", TranslatedOld);
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        var result = await CreateService(SystemRepository([existing]), queue).UpdateAsync(
            existing.Id,
            new UpdateAdminStyleArtPresetRequestDto("Neon", "Old description", "new modifiers", "[\"Posters\"]", null, false, false));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be(StyleArtPresetTranslations.Translated);
        result.Value.Vi!.Name.Should().Be("Ten cu");
        existing.Translations.Should().Be(TranslatedOld);
        queue.Verify(x => x.Notify(), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenTheLastTranslationFailed_QueuesItAgain()
    {
        var existing = SystemPreset("Neon", "{\"status\":\"failed\",\"attempts\":3}");
        var queue = new Mock<IStyleArtPresetTranslationQueue>();

        await CreateService(SystemRepository([existing]), queue).UpdateAsync(
            existing.Id,
            new UpdateAdminStyleArtPresetRequestDto("Neon", "Old description", "old", "[\"Posters\"]", null, false, true));

        var state = StyleArtPresetTranslations.ParseState(existing.Translations)!;
        state.Status.Should().Be(StyleArtPresetTranslations.Pending);
        state.Attempts.Should().Be(0);
        queue.Verify(x => x.Notify(), Times.Once);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenSystemPreset_DeactivatesAndInvalidatesCache()
    {
        var existing = SystemPreset("Neon", TranslatedOld);
        var cache = MissCache();

        var result = await CreateService(SystemRepository([existing]), cache: cache).DeleteAsync(existing.Id);

        result.IsSuccess.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        cache.Verify(x => x.RemoveAsync(StyleArtPresetCacheKeys.SystemRows, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ListAsync_ReturnsTheTranslationAndItsStatus()
    {
        var translated = SystemPreset("Translated", TranslatedOld);
        var pending = SystemPreset("Pending", "{\"status\":\"pending\",\"attempts\":1,\"nextRetryAt\":\"2026-11-14T05:00:00.0000000Z\"}");
        var untouched = SystemPreset("Untouched", null);

        var result = await CreateService(SystemRepository([translated, pending, untouched])).ListAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Single(p => p.Name == "Translated").ViStatus.Should().Be(StyleArtPresetTranslations.Translated);
        result.Value.Single(p => p.Name == "Translated").Vi!.Name.Should().Be("Ten cu");
        result.Value.Single(p => p.Name == "Pending").ViStatus.Should().Be(StyleArtPresetTranslations.Pending);
        result.Value.Single(p => p.Name == "Pending").Vi.Should().BeNull();
        result.Value.Single(p => p.Name == "Untouched").ViStatus.Should().BeNull();
    }

    private static StyleArtPreset SystemPreset(string name, string? translations) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Old description",
            StyleModifiers = "old",
            Recommendations = "[\"Posters\"]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = translations
        };

    private static AdminStyleArtPresetService CreateService(
        Mock<IRepository<StyleArtPreset>> repository,
        Mock<IStyleArtPresetTranslationQueue>? queue = null,
        Mock<ICacheService>? cache = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(Guid.NewGuid());

        return new AdminStyleArtPresetService(
            currentUser.Object,
            repository.Object,
            new Mock<IPublicImageService>().Object,
            (cache ?? MissCache()).Object,
            (queue ?? new Mock<IStyleArtPresetTranslationQueue>()).Object,
            TimeProvider.System);
    }

    private static Mock<ICacheService> MissCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return cache;
    }

    private static Mock<IRepository<StyleArtPreset>> SystemRepository(StyleArtPreset[] rows)
    {
        var repository = new Mock<IRepository<StyleArtPreset>>();
        repository.Setup(r => r.Query()).Returns(rows.AsQueryable().BuildMock());
        return repository;
    }
}
