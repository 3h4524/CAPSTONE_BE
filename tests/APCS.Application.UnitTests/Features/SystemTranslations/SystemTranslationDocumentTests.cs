using APCS.Application.Features.SystemTranslations;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.SystemTranslations;

[TestClass]
public sealed class SystemTranslationDocumentTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["label"] = "Daily video safety cap",
        ["group"] = "Video"
    };

    private static readonly IReadOnlyDictionary<string, string> Vi = new Dictionary<string, string>
    {
        ["label"] = "Giới hạn an toàn video hàng ngày",
        ["group"] = "Video"
    };

    [TestMethod]
    public void Localizer_WhenSourceTextMatches_ReturnsVietnamese()
    {
        var translations = SystemTranslationDocument.Succeeded(Fields, Vi);

        var localizer = SystemTextLocalizer.For(translations, "vi");

        localizer.Text("label", "Daily video safety cap").Should().Be("Giới hạn an toàn video hàng ngày");
    }

    [TestMethod]
    public void Localizer_WhenEnglishChangedSinceTranslation_ReturnsCurrentEnglish()
    {
        var translations = SystemTranslationDocument.Succeeded(Fields, Vi);

        var localizer = SystemTextLocalizer.For(translations, "vi");

        localizer.Text("label", "Daily video cap").Should().Be("Daily video cap");
    }

    [TestMethod]
    public void Localizer_WhenCultureIsEnglish_ReturnsEnglish()
    {
        var translations = SystemTranslationDocument.Succeeded(Fields, Vi);

        SystemTextLocalizer.For(translations, "en").Text("label", "Daily video safety cap")
            .Should().Be("Daily video safety cap");
    }

    [TestMethod]
    public void Localizer_WhenDocumentIsCorrupt_ReturnsEnglish()
    {
        SystemTextLocalizer.For("{not-json", "vi").Text("label", "Daily video safety cap")
            .Should().Be("Daily video safety cap");
    }

    [TestMethod]
    public void Localizer_WhenEnglishIsNull_ReturnsNull()
    {
        var translations = SystemTranslationDocument.Succeeded(Fields, Vi);

        SystemTextLocalizer.For(translations, "vi").Text("label", null).Should().BeNull();
    }

    [TestMethod]
    public void NeedsTranslation_WhenNeverTranslated_ReturnsTrue()
    {
        SystemTranslationDocument.NeedsTranslation(null, Fields, Now).Should().BeTrue();
    }

    [TestMethod]
    public void NeedsTranslation_WhenTranslatedFromCurrentText_ReturnsFalse()
    {
        SystemTranslationDocument.NeedsTranslation(SystemTranslationDocument.Succeeded(Fields, Vi), Fields, Now).Should().BeFalse();
    }

    [TestMethod]
    public void NeedsTranslation_WhenEnglishChanged_ReturnsTrue()
    {
        var changed = new Dictionary<string, string>(Fields) { ["label"] = "Daily video cap" };

        SystemTranslationDocument.NeedsTranslation(SystemTranslationDocument.Succeeded(Fields, Vi), changed, Now).Should().BeTrue();
    }

    [TestMethod]
    public void NeedsTranslation_WhenRowHasNoText_ReturnsFalse()
    {
        SystemTranslationDocument.NeedsTranslation(null, new Dictionary<string, string>(), Now).Should().BeFalse();
    }

    [TestMethod]
    public void AfterFailedAttempt_WaitsBeforeTheNextTry()
    {
        var failed = SystemTranslationDocument.AfterFailedAttempt(null, Fields, Now);

        SystemTranslationDocument.NeedsTranslation(failed, Fields, Now).Should().BeFalse();
        SystemTranslationDocument.NeedsTranslation(failed, Fields, Now.AddMinutes(5)).Should().BeTrue();
        SystemTranslationDocument.Parse(failed)!.Status.Should().Be(SystemTranslationDocument.Pending);
    }

    [TestMethod]
    public void AfterFailedAttempt_AfterEveryRetry_MarksFailedAndRetriesLater()
    {
        string? translations = null;
        for (var attempt = 0; attempt < 4; attempt++)
            translations = SystemTranslationDocument.AfterFailedAttempt(translations, Fields, Now);

        var document = SystemTranslationDocument.Parse(translations)!;
        document.Status.Should().Be(SystemTranslationDocument.Failed);
        document.Attempts.Should().Be(4);
        document.NextRetryAt.Should().Be(Now.AddHours(6));
    }

    [TestMethod]
    public void AfterFailedAttempt_KeepsOnlyTranslationsOfUnchangedFields()
    {
        var changed = new Dictionary<string, string>(Fields) { ["label"] = "Daily video cap" };

        var failed = SystemTranslationDocument.AfterFailedAttempt(SystemTranslationDocument.Succeeded(Fields, Vi), changed, Now);

        var localizer = SystemTextLocalizer.For(failed, "vi");
        localizer.Text("label", "Daily video cap").Should().Be("Daily video cap");
        localizer.Text("group", "Video").Should().Be("Video");
        SystemTranslationDocument.Parse(failed)!.Vi.Keys.Should().Equal("group");
    }

    [TestMethod]
    public void Fields_ForVideoPack_ReadsTheDescriptionFromThePack()
    {
        var template = new VideoTemplate
        {
            Name = "Rotating Showcase",
            EffectsConfig = "{\"videoPack\":{\"description\":\"Four rotating camera moves.\",\"scenes\":[]}}"
        };

        SystemTranslationFields.For(template).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["name"] = "Rotating Showcase",
            ["description"] = "Four rotating camera moves."
        });
    }

    [TestMethod]
    public void Fields_ForAiVideoTemplate_ReadsNameDescriptionAndSubtemplates()
    {
        var template = new VideoTemplate
        {
            Name = "Product On Model",
            EffectsConfig = "{\"aiVideo\":{\"name\":\"Product On Model\",\"description\":\"Fashion videos.\",\"basePrompt\":\"prompt\"," +
                "\"subtemplates\":[{\"code\":\"natural_motion\",\"name\":\"Natural Motion\",\"description\":\"A balanced shot.\",\"basePrompt\":\"prompt\"}]}}"
        };

        SystemTranslationFields.For(template).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["name"] = "Product On Model",
            ["aiName"] = "Product On Model",
            ["description"] = "Fashion videos.",
            ["subtemplates.natural_motion.name"] = "Natural Motion",
            ["subtemplates.natural_motion.description"] = "A balanced shot."
        });
    }

    [TestMethod]
    public void Fields_ForDesignTemplate_SkipsEmptyText()
    {
        var template = new DesignTemplate { Name = "Blue Garden", StyleDescription = " " };

        SystemTranslationFields.For(template).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["name"] = "Blue Garden"
        });
    }
}
