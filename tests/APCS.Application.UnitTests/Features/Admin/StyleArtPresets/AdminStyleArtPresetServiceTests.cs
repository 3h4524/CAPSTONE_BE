using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Admin.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets;
using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using System.Text.Json;

namespace APCS.Application.UnitTests.Features.Admin.StyleArtPresets;

[TestClass]
public sealed class AdminStyleArtPresetServiceTests
{
    [TestMethod]
    public async Task CreateAsync_WhenEnOnly_AutoTranslatesAndStoresVi()
    {
        var repository = SystemRepository([]);
        StyleArtPreset? captured = null;
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), true, It.IsAny<CancellationToken>()))
            .Callback<StyleArtPreset, bool, CancellationToken>((preset, _, _) => captured = preset)
            .Returns(Task.CompletedTask);
        var translationService = TranslatorReturning(new Dictionary<string, string>
        {
            ["name"] = "Neon chu",
            ["description"] = "Mo manh liet",
            ["recommendations/0"] = "Ap phich"
        });

        var result = await CreateService(repository, translationService.Object).CreateAsync(
            new CreateAdminStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"Posters\"]", null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.Vi.Should().NotBeNull();
        result.Value.ViStatus.Should().Be("ok");
        repository.Verify(r => r.AddAsync(It.IsAny<StyleArtPreset>(), true, It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        var vi = JsonDocument.Parse(captured!.Translations!).RootElement;
        vi.GetProperty("status").GetString().Should().Be("ok");
        vi.GetProperty("manual").GetBoolean().Should().BeFalse();
        vi.GetProperty("attempts").GetInt32().Should().Be(0);
        vi.GetProperty("vi").GetProperty("name").GetString().Should().Be("Neon chu");
    }

    [TestMethod]
    public async Task CreateAsync_WhenFormViProvided_StoresManualWithoutCallingGoogle()
    {
        var repository = SystemRepository([]);
        StyleArtPreset? captured = null;
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), true, It.IsAny<CancellationToken>()))
            .Callback<StyleArtPreset, bool, CancellationToken>((preset, _, _) => captured = preset)
            .Returns(Task.CompletedTask);
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);

        var result = await CreateService(repository, translationService.Object).CreateAsync(
            new CreateAdminStyleArtPresetRequestDto(
                "Neon", "Bold glow", "neon glow", "[\"Posters\"]", null,
                new AdminStyleArtPresetViRequestDto("Neon chu", "Mo manh liet", "[\"Ap phich\"]")));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be("manual");
        translationService.Verify(
            x => x.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var vi = JsonDocument.Parse(captured!.Translations!).RootElement;
        vi.GetProperty("manual").GetBoolean().Should().BeTrue();
        vi.GetProperty("vi").GetProperty("name").GetString().Should().Be("Neon chu");
    }

    [TestMethod]
    public async Task CreateAsync_WhenGoogleFails_SavesAsPendingWithoutBlocking()
    {
        var repository = SystemRepository([]);
        StyleArtPreset? captured = null;
        repository.Setup(r => r.AddAsync(It.IsAny<StyleArtPreset>(), true, It.IsAny<CancellationToken>()))
            .Callback<StyleArtPreset, bool, CancellationToken>((preset, _, _) => captured = preset)
            .Returns(Task.CompletedTask);
        var translationService = TranslatorReturning(new Dictionary<string, string>(), isTranslated: false);

        var result = await CreateService(repository, translationService.Object).CreateAsync(
            new CreateAdminStyleArtPresetRequestDto("Neon", "Bold glow", "neon glow", "[\"Posters\"]", null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be("pending");
        var vi = JsonDocument.Parse(captured!.Translations!).RootElement;
        vi.GetProperty("status").GetString().Should().Be("pending");
        vi.GetProperty("attempts").GetInt32().Should().Be(1);
        vi.GetProperty("nextRetryAt").GetString().Should().NotBeNullOrWhiteSpace();
        vi.TryGetProperty("vi", out _).Should().BeFalse();
    }

    [TestMethod]
    public async Task UpdateAsync_WhenGoogleFails_KeepsPreviousViAndMarksPending()
    {
        var presetId = Guid.NewGuid();
        var existing = new StyleArtPreset
        {
            Id = presetId,
            Name = "Old name",
            Description = "Old description",
            StyleModifiers = "old",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            UsageCount = 5,
            Translations = "{\"vi\":{\"name\":\"Ten cu\",\"description\":\"Mo ta cu\",\"recommendations\":[]},\"status\":\"ok\",\"attempts\":0}"
        };
        var repository = SystemRepository([existing]);
        var translationService = TranslatorReturning(new Dictionary<string, string>(), isTranslated: false);

        var result = await CreateService(repository, translationService.Object).UpdateAsync(
            presetId,
            new UpdateAdminStyleArtPresetRequestDto("New name", "New description", "new", "[]", null, false, true, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be("pending");
        result.Value.Vi!.Name.Should().Be("Ten cu");
        var vi = JsonDocument.Parse(existing.Translations!).RootElement;
        vi.GetProperty("status").GetString().Should().Be("pending");
        vi.GetProperty("attempts").GetInt32().Should().Be(1);
        vi.GetProperty("vi").GetProperty("name").GetString().Should().Be("Ten cu");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenViAllEmpty_ClearsTranslations()
    {
        var presetId = Guid.NewGuid();
        var existing = new StyleArtPreset
        {
            Id = presetId,
            Name = "Old name",
            Description = "Old description",
            StyleModifiers = "old",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = "{\"vi\":{\"name\":\"Ten cu\",\"description\":\"Mo ta cu\",\"recommendations\":[]},\"manual\":true,\"status\":\"ok\",\"attempts\":0}"
        };
        var repository = SystemRepository([existing]);
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);

        var result = await CreateService(repository, translationService.Object).UpdateAsync(
            presetId,
            new UpdateAdminStyleArtPresetRequestDto(
                "New name", "New description", "new", "[]", null, false, true,
                new AdminStyleArtPresetViRequestDto("", "", "")));

        result.IsSuccess.Should().BeTrue();
        result.Value.Vi.Should().BeNull();
        result.Value.ViStatus.Should().BeNull();
        existing.Translations.Should().BeNull();
        translationService.Verify(
            x => x.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenPreviousViIsManual_KeepsManualWithoutCallingGoogle()
    {
        var presetId = Guid.NewGuid();
        var existing = new StyleArtPreset
        {
            Id = presetId,
            Name = "Old name",
            Description = "Old description",
            StyleModifiers = "old",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = "{\"vi\":{\"name\":\"Sua tay\",\"description\":\"Mo ta sua tay\",\"recommendations\":[]},\"manual\":true,\"status\":\"ok\",\"attempts\":0}"
        };
        var repository = SystemRepository([existing]);
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);

        var result = await CreateService(repository, translationService.Object).UpdateAsync(
            presetId,
            new UpdateAdminStyleArtPresetRequestDto("New name", "New description", "new", "[]", null, false, true, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.ViStatus.Should().Be("manual");
        result.Value.Vi!.Name.Should().Be("Sua tay");
        translationService.Verify(
            x => x.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_WhenPreviousViIsManual_KeepsManualVi_WithoutCallingGoogle()
    {
        var presetId = Guid.NewGuid();
        var existing = new StyleArtPreset
        {
            Id = presetId,
            Name = "Old name",
            Description = "Old description",
            StyleModifiers = "old",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = "{\"vi\":{\"name\":\"Ten sua tay\",\"description\":\"Mo ta cu\",\"recommendations\":[]},\"manual\":true,\"status\":\"ok\",\"attempts\":0}"
        };
        var repository = SystemRepository([existing]);
        var translationService = new Mock<ITranslationService>(MockBehavior.Strict);

        var result = await CreateService(repository, translationService.Object).UpdateAsync(
            presetId,
            new UpdateAdminStyleArtPresetRequestDto("New name", "New description", "new", "[]", null, false, true, null));

        result.IsSuccess.Should().BeTrue();
        result.Value.Vi!.Name.Should().Be("Ten sua tay");
        result.Value.ViStatus.Should().Be("manual");
        translationService.Verify(
            x => x.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var vi = JsonDocument.Parse(existing.Translations!).RootElement;
        vi.GetProperty("vi").GetProperty("name").GetString().Should().Be("Ten sua tay");
    }

    [TestMethod]
    public async Task DeleteAsync_WhenSystemPreset_DeactivatesAndInvalidatesCache()
    {
        var presetId = Guid.NewGuid();
        var existing = new StyleArtPreset
        {
            Id = presetId,
            Name = "Neon",
            Description = "Glow",
            StyleModifiers = "neon",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true
        };
        var repository = SystemRepository([existing]);
        var cache = new Mock<ICacheService>();

        var result = await CreateService(repository, new Mock<ITranslationService>().Object, cache: cache).DeleteAsync(presetId);

        result.IsSuccess.Should().BeTrue();
        existing.IsActive.Should().BeFalse();
        cache.Verify(x => x.RemoveAsync(StyleArtPresetCacheKeys.SystemRows, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ListAsync_ReturnsViAndViStatus()
    {
        var manual = new StyleArtPreset
        {
            Id = Guid.NewGuid(),
            Name = "Manual",
            Description = "D",
            StyleModifiers = "m",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = "{\"vi\":{\"name\":\"Sua tay\",\"description\":\"Mo ta\",\"recommendations\":[]},\"manual\":true,\"status\":\"ok\",\"attempts\":0}"
        };
        var pending = new StyleArtPreset
        {
            Id = Guid.NewGuid(),
            Name = "Pending",
            Description = "D",
            StyleModifiers = "m",
            Recommendations = "[]",
            IsActive = true,
            IsSystemTemplate = true,
            Translations = "{\"status\":\"pending\",\"attempts\":1,\"nextRetryAt\":\"2026-11-14T05:00:00.0000000Z\"}"
        };
        var repository = SystemRepository([manual, pending]);

        var result = await CreateService(repository, new Mock<ITranslationService>().Object).ListAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Single(p => p.Name == "Manual").ViStatus.Should().Be("manual");
        result.Value.Single(p => p.Name == "Pending").ViStatus.Should().Be("pending");
        result.Value.Single(p => p.Name == "Pending").Vi.Should().BeNull();
    }

    private static Mock<ITranslationService> TranslatorReturning(Dictionary<string, string> translated, bool isTranslated = true)
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(x => x.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslateTextsRequestDto request, CancellationToken _) =>
            {
                var items = request.Items
                    .Select(item => translated.TryGetValue(item.Key, out var text)
                        ? new TranslatedTextDto(item.Key, text, isTranslated, false)
                        : new TranslatedTextDto(item.Key, item.Text, isTranslated, false))
                    .ToArray();
                return Result.Success(new TranslateTextsResponseDto(items));
            });
        return translationService;
    }

    private static AdminStyleArtPresetService CreateService(
        Mock<IRepository<StyleArtPreset>> repository,
        ITranslationService translationService,
        Mock<ICacheService>? cache = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(Guid.NewGuid());

        var cacheMock = cache ?? MissCache();

        return new AdminStyleArtPresetService(
            currentUser.Object,
            repository.Object,
            new Mock<IPublicImageService>().Object,
            cacheMock.Object,
            translationService,
            new Mock<ILogger<AdminStyleArtPresetService>>().Object,
            TimeProvider.System);
    }

    private static Mock<ICacheService> MissCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(x => x.GetAsync<IReadOnlyList<StyleArtPresetCacheRow>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StyleArtPresetCacheRow>?)null);
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<List<StyleArtPresetCacheRow>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
