using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Translation;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.SystemTranslations;
using APCS.Application.Features.Translations;
using APCS.Application.Features.Translations.Dtos.Request;
using APCS.Application.Features.Translations.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace APCS.Infrastructure.UnitTests.BackgroundServices;

[TestClass]
public sealed class SystemDataTranslationWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string databaseName = Guid.NewGuid().ToString();
    private readonly Mock<ITranslationProvider> provider = new();
    private readonly Mock<ITranslationService> translator = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly Mock<IFeatureCatalog> featureCatalog = new();

    [TestInitialize]
    public void SetUp()
    {
        provider.SetupGet(candidate => candidate.IsConfigured).Returns(true);
        translator
            .Setup(candidate => candidate.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslateTextsRequestDto request, CancellationToken _) => Result.Success(new TranslateTextsResponseDto(
                request.Items.Select(item => new TranslatedTextDto(item.Key, $"vi:{item.Text}", true, false)).ToArray())));
    }

    [TestMethod]
    public async Task SweepAsync_TranslatesUntranslatedRowsAndInvalidatesTheirCaches()
    {
        await SeedAsync(Feature("Daily video safety cap"), Template("Blue Garden"));

        await Worker().SweepAsync(CancellationToken.None);

        await using var db = Context();
        var feature = await db.Features.SingleAsync();
        SystemTextLocalizer.For(feature.Translations, "vi").Text(SystemTranslationFields.Label, feature.Label)
            .Should().Be("vi:Daily video safety cap");
        var template = await db.DesignTemplates.SingleAsync();
        SystemTextLocalizer.For(template.Translations, "vi").Text(SystemTranslationFields.Name, template.Name)
            .Should().Be("vi:Blue Garden");
        featureCatalog.Verify(candidate => candidate.Invalidate(), Times.Once);
        cache.Verify(candidate => candidate.RemoveAsync(DesignTemplateCacheKeys.SystemCatalog, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SweepAsync_WhenTranslationIsCurrent_DoesNotCallTheProvider()
    {
        await SeedAsync(Feature("Daily video safety cap"));
        await Worker().SweepAsync(CancellationToken.None);
        translator.Invocations.Clear();

        await Worker().SweepAsync(CancellationToken.None);

        translator.Verify(candidate => candidate.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SweepAsync_WhenEnglishChanged_TranslatesAgain()
    {
        await SeedAsync(Feature("Daily video safety cap"));
        await Worker().SweepAsync(CancellationToken.None);
        await using (var db = Context())
        {
            (await db.Features.SingleAsync()).Label = "Daily video cap";
            await db.SaveChangesAsync();
        }

        await Worker().SweepAsync(CancellationToken.None);

        await using var check = Context();
        var feature = await check.Features.SingleAsync();
        SystemTextLocalizer.For(feature.Translations, "vi").Text(SystemTranslationFields.Label, feature.Label)
            .Should().Be("vi:Daily video cap");
    }

    [TestMethod]
    public async Task SweepAsync_WhenProviderIsNotConfigured_WritesNothing()
    {
        provider.SetupGet(candidate => candidate.IsConfigured).Returns(false);
        await SeedAsync(Feature("Daily video safety cap"));

        await Worker().SweepAsync(CancellationToken.None);

        await using var db = Context();
        (await db.Features.SingleAsync()).Translations.Should().BeNull();
    }

    [TestMethod]
    public async Task SweepAsync_WhenTranslationFails_QueuesTheRowForRetry()
    {
        translator
            .Setup(candidate => candidate.TranslateAsync(It.IsAny<TranslateTextsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslateTextsRequestDto request, CancellationToken _) => Result.Success(new TranslateTextsResponseDto(
                request.Items.Select(item => new TranslatedTextDto(item.Key, item.Text, false, false)).ToArray())));
        await SeedAsync(Feature("Daily video safety cap"));

        await Worker().SweepAsync(CancellationToken.None);

        await using var db = Context();
        var document = SystemTranslationDocument.Parse((await db.Features.SingleAsync()).Translations)!;
        document.Status.Should().Be(SystemTranslationDocument.Pending);
        document.NextRetryAt.Should().Be(Now.UtcDateTime.AddMinutes(5));
        featureCatalog.Verify(candidate => candidate.Invalidate(), Times.Never);
    }

    private SystemDataTranslationWorker Worker()
    {
        var services = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddScoped(_ => provider.Object)
            .AddScoped(_ => translator.Object)
            .AddScoped(_ => cache.Object)
            .AddScoped(_ => featureCatalog.Object)
            .BuildServiceProvider();
        return new SystemDataTranslationWorker(services, new FakeTimeProvider(Now), NullLogger<SystemDataTranslationWorker>.Instance);
    }

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

    private async Task SeedAsync(params object[] rows)
    {
        await using var db = Context();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static Feature Feature(string label) => new()
    {
        Code = "daily_video_cap",
        Label = label,
        Description = "Most standard videos rendered per day.",
        GroupName = "Video",
        Kind = "limit",
        IsActive = true,
        TrialAvailable = true
    };

    private static DesignTemplate Template(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = "system",
        BasePrompt = "prompt",
        ExamplePrompts = "[]",
        IsSystemTemplate = true,
        IsActive = true
    };
}
