using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Moderation;

[TestClass]
public sealed class ContentModerationServiceTests
{
    private static readonly ModerationContext Input =
        new(Guid.NewGuid(), ModerationFeatures.DesignGeneration, ModerationDirection.Input, ModerationSubjects.Product);

    private static readonly ModerationContext Output =
        new(Guid.NewGuid(), ModerationFeatures.DesignGeneration, ModerationDirection.Output, ModerationSubjects.DesignImage, Guid.NewGuid());

    private readonly Mock<IModerationStore> store = new();
    private readonly Mock<IGeminiTextGenerationProvider> gemini = new();
    private readonly Mock<ICacheService> cache = new();
    private readonly List<ModerationCheckRecord> recorded = [];

    [TestInitialize]
    public void SetUp()
    {
        store.Setup(x => x.LoadListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ModerationLists(
            [
                new ModerationTermEntry("porn", ModerationCategories.Sexual, "block", "word", false),
                new ModerationTermEntry("nude", ModerationCategories.Sexual, "flag", "word", false)
            ],
            []));
        store.Setup(x => x.AddCheckAsync(It.IsAny<ModerationCheckRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ModerationCheckRecord, CancellationToken>((record, _) => recorded.Add(record))
            .Returns(Task.CompletedTask);
        gemini.SetupGet(x => x.IsConfigured).Returns(true);
    }

    [TestMethod]
    public async Task CheckTextAsync_BlockingTerm_BlocksWithoutAskingGemini()
    {
        var verdict = await Service().CheckTextAsync([new ModerationText("the design text", "Retro P0RN poster")], Input);

        verdict.Decision.Should().Be(ModerationDecision.Block);
        verdict.DecidedBy.Should().Be(ModerationDeciders.Blocklist);
        verdict.Findings.Should().ContainSingle(finding => finding.Field == "the design text");
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        recorded.Should().ContainSingle(record => record.Decision == "block" && record.DecidedBy == "blocklist" && record.Direction == "input");
    }

    [TestMethod]
    public async Task CheckTextAsync_FlaggedTerm_AsksGeminiWithTheHintAndFollowsThePolicy()
    {
        GeminiAnswers("""{"findings":[{"category":"sexual","severity":"medium"}],"reason":"Suggestive."}""");

        var verdict = await Service().CheckTextAsync([new ModerationText("the product name", "nude figure study")], Input);

        verdict.Decision.Should().Be(ModerationDecision.Flagged);
        verdict.IsBlocked.Should().BeFalse();
        gemini.Verify(x => x.GenerateAsync(
            It.Is<GeminiTextGenerationRequest>(request => request.Prompt.Contains("\"nude\" (sexual)") &&
                                                          request.Model == "gemini-3.5-flash-lite" && request.Temperature == 0m),
            It.IsAny<CancellationToken>()), Times.Once);
        recorded.Should().ContainSingle(record => record.Decision == "flagged" && record.Model == "gemini-3.5-flash-lite");
    }

    [TestMethod]
    public async Task CheckTextAsync_CachedVerdict_IsReusedWithoutGeminiOrLog()
    {
        cache.Setup(x => x.GetAsync<ModerationVerdict>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModerationVerdict.Allowed(ModerationDeciders.Gemini));

        var verdict = await Service().CheckTextAsync([new ModerationText("the product name", "Cozy cat tee")], Input);

        verdict.Decision.Should().Be(ModerationDecision.Allow);
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        recorded.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CheckTextAsync_GeminiSafetyFilterRefuses_Blocks()
    {
        gemini.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GeminiContentBlockedException("PROHIBITED_CONTENT"));

        var verdict = await Service().CheckTextAsync([new ModerationText("the custom image prompt", "something awful")], Input);

        verdict.Decision.Should().Be(ModerationDecision.Block);
        verdict.DecidedBy.Should().Be(ModerationDeciders.ProviderSafety);
    }

    [TestMethod]
    public async Task CheckTextAsync_GeminiUnavailable_LetsTheTextThroughButRecordsItUnchecked()
    {
        gemini.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var verdict = await Service().CheckTextAsync([new ModerationText("the product name", "Cozy cat tee")], Input);

        verdict.Decision.Should().Be(ModerationDecision.Unchecked);
        verdict.IsBlocked.Should().BeFalse();
        recorded.Should().ContainSingle(record => record.Decision == "unchecked" && record.DecidedBy == "error");
        cache.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<ModerationVerdict>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckTextAsync_BlocklistOnly_DoesNotAskGemini()
    {
        var verdict = await Service().CheckTextAsync([new ModerationText("the product name", "nude figure study")], Input, blocklistOnly: true);

        verdict.IsBlocked.Should().BeFalse();
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckTextAsync_ClassifierDisabled_RunsOnlyTheBlocklist()
    {
        var service = Service(enabled: false);

        (await service.CheckTextAsync([new ModerationText("the product name", "nude figure")], Input)).IsBlocked.Should().BeFalse();
        (await service.CheckTextAsync([new ModerationText("the product name", "porn")], Input)).IsBlocked.Should().BeTrue();
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckTextAsync_NoTextLeft_IsAllowedWithoutAnyCheck()
    {
        var verdict = await Service().CheckTextAsync([new ModerationText("the product name", "  "), new ModerationText("the niche", null)], Input);

        verdict.Decision.Should().Be(ModerationDecision.Allow);
        store.Verify(x => x.LoadListsAsync(It.IsAny<CancellationToken>()), Times.Never);
        recorded.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CheckImageAsync_SendsTheImageAndRecordsTheSubject()
    {
        GeminiAnswers("""{"findings":[{"category":"sexual","severity":"high"}]}""");

        var verdict = await Service().CheckImageAsync([1, 2, 3], "image/png", Output);

        verdict.IsBlocked.Should().BeTrue();
        gemini.Verify(x => x.GenerateAsync(It.Is<GeminiTextGenerationRequest>(request => request.Images!.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        recorded.Should().ContainSingle(record => record.Direction == "output" && record.SubjectId == Output.SubjectId &&
                                                  record.SubjectType == ModerationSubjects.DesignImage);
    }

    [TestMethod]
    public async Task CheckImageAsync_GeminiUnavailable_IsUnchecked()
    {
        gemini.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Gemini returned an empty generation."));

        (await Service().CheckImageAsync([1, 2, 3], "image/png", Output)).Decision.Should().Be(ModerationDecision.Unchecked);
    }

    [TestMethod]
    public async Task CheckImageAsync_ClassifierDisabled_IsAllowedAndNotRecorded()
    {
        (await Service(enabled: false).CheckImageAsync([1, 2, 3], "image/png", Output)).Decision.Should().Be(ModerationDecision.Allow);
        recorded.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CheckTextAsync_LogCannotBeWritten_TheDecisionStands()
    {
        store.Setup(x => x.AddCheckAsync(It.IsAny<ModerationCheckRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        (await Service().CheckTextAsync([new ModerationText("the design text", "porn")], Input)).IsBlocked.Should().BeTrue();
    }

    private void GeminiAnswers(string json) =>
        gemini.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerationResult(json, "gemini-3.5-flash-lite", 100, 20, null));

    [TestMethod]
    public async Task CheckTextAsync_CategorySetToBlockFromMedium_BlocksAMediumRating()
    {
        Settings(new Dictionary<string, string> { ["block_at.sexual"] = "medium" });
        GeminiAnswers("""{"findings":[{"category":"sexual","severity":"medium"}]}""");

        (await Service().CheckTextAsync([new ModerationText("the product name", "suggestive slang")], Input))
            .Decision.Should().Be(ModerationDecision.Block);
    }

    [TestMethod]
    public async Task CheckTextAsync_OtherCategoryAtMedium_IsStillOnlyFlagged()
    {
        Settings(new Dictionary<string, string> { ["block_at.sexual"] = "medium" });
        GeminiAnswers("""{"findings":[{"category":"violence_gore","severity":"medium"}]}""");

        (await Service().CheckTextAsync([new ModerationText("the product name", "zombie horror")], Input))
            .Decision.Should().Be(ModerationDecision.Flagged);
    }

    [TestMethod]
    public async Task CheckTextAsync_GeminiSwitchedOffByAnAdministrator_RunsOnlyTheBlocklist()
    {
        Settings(new Dictionary<string, string> { ["enabled"] = "false" });

        (await Service().CheckTextAsync([new ModerationText("the product name", "nude figure")], Input)).IsBlocked.Should().BeFalse();
        (await Service().CheckTextAsync([new ModerationText("the product name", "porn")], Input)).IsBlocked.Should().BeTrue();
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CheckTextAsync_TextIsKeptOnlyWhenNotAllowed()
    {
        GeminiAnswers("""{"findings":[]}""");
        await Service().CheckTextAsync([new ModerationText("the product name", "Cozy cat tee")], Input);
        await Service().CheckTextAsync([new ModerationText("the design text", "porn poster"), new ModerationText("the niche", "art")], Input);

        recorded.Should().HaveCount(2);
        recorded[0].ContentExcerpt.Should().BeNull();
        recorded[1].ContentExcerpt.Should().Be("the design text: porn poster\nthe niche: art");
    }

    [TestMethod]
    public void Excerpt_LongText_IsCutTo500Characters()
    {
        var excerpt = ContentModerationService.Excerpt([new ModerationText("the custom image prompt", new string('a', 900))]);

        excerpt.Should().HaveLength(ContentModerationService.MaximumExcerptLength).And.EndWith("…");
    }

    [TestMethod]
    public async Task CheckImageAsync_BlockedImage_KeepsNoExcerpt()
    {
        GeminiAnswers("""{"findings":[{"category":"sexual","severity":"high"}]}""");

        await Service().CheckImageAsync([1, 2, 3], "image/png", Output);

        recorded.Should().ContainSingle().Which.ContentExcerpt.Should().BeNull();
    }

    [TestMethod]
    public async Task CheckTextAsync_ListsOrSettingsChange_UseAnotherCacheKey()
    {
        var keys = new List<string>();
        cache.Setup(x => x.GetAsync<ModerationVerdict>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => keys.Add(key))
            .ReturnsAsync((ModerationVerdict?)null);
        GeminiAnswers("""{"findings":[]}""");
        var options = Options.Create(new ModerationOptions());
        var blocklist = new ModerationBlocklistCache(store.Object, options, new FakeTimeProvider(), NullLogger<ModerationBlocklistCache>.Instance);
        var runtime = new ModerationRuntimeSettings(store.Object, options, new FakeTimeProvider(), NullLogger<ModerationRuntimeSettings>.Instance);
        var service = new ContentModerationService(blocklist, runtime, store.Object, gemini.Object, cache.Object, options,
            NullLogger<ContentModerationService>.Instance);
        var text = new[] { new ModerationText("the product name", "Cozy cat tee") };

        await service.CheckTextAsync(text, Input);
        await service.CheckTextAsync(text, Input);
        store.Setup(x => x.LoadListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ModerationLists(
            [new ModerationTermEntry("nứng", ModerationCategories.Sexual, "block", "word", false)], []));
        blocklist.Invalidate();
        await service.CheckTextAsync(text, Input);
        Settings(new Dictionary<string, string> { ["block_at.sexual"] = "medium" });
        runtime.Invalidate();
        await service.CheckTextAsync(text, Input);

        keys.Should().HaveCount(4);
        keys[1].Should().Be(keys[0]);
        keys[2].Should().NotBe(keys[1]);
        keys[3].Should().NotBe(keys[2]);
    }

    private void Settings(IReadOnlyDictionary<string, string> values) =>
        store.Setup(x => x.LoadSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(values);

    private ContentModerationService Service(bool enabled = true)
    {
        var options = Options.Create(new ModerationOptions { Enabled = enabled });
        var blocklist = new ModerationBlocklistCache(store.Object, options, new FakeTimeProvider(),
            NullLogger<ModerationBlocklistCache>.Instance);
        var runtime = new ModerationRuntimeSettings(store.Object, options, new FakeTimeProvider(),
            NullLogger<ModerationRuntimeSettings>.Instance);
        return new ContentModerationService(blocklist, runtime, store.Object, gemini.Object, cache.Object, options,
            NullLogger<ContentModerationService>.Instance);
    }
}
