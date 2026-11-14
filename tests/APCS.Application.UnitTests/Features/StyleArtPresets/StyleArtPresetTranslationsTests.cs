using System.Text.Json;
using APCS.Application.Features.StyleArtPresets.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.StyleArtPresets;

[TestClass]
public sealed class StyleArtPresetTranslationsTests
{
    [TestMethod]
    public void Localize_WhenViTranslationPresent_ReturnsViContent()
    {
        var translations = "{\"vi\":{\"name\":\"Co dien\",\"description\":\"Tong co\",\"recommendations\":[\"Ap phich\"]},\"status\":\"ok\",\"attempts\":0}";

        var result = StyleArtPresetTranslations.Localize("Vintage", "Retro tones", "[\"Posters\"]", translations, "vi");

        result.Name.Should().Be("Co dien");
        result.Description.Should().Be("Tong co");
        result.Recommendations.Should().Be("[\"Ap phich\"]");
    }

    [TestMethod]
    public void Localize_WhenFieldsMissing_FallsBackToEnglishPerField()
    {
        var translations = "{\"vi\":{\"name\":\"Co dien\",\"description\":\"\",\"recommendations\":[]},\"status\":\"ok\"}";

        var result = StyleArtPresetTranslations.Localize("Vintage", "Retro tones", "[\"Posters\"]", translations, "vi");

        result.Name.Should().Be("Co dien");
        result.Description.Should().Be("Retro tones");
        result.Recommendations.Should().Be("[\"Posters\"]");
    }

    [TestMethod]
    public void Localize_WhenJsonIsCorrupt_FallsBackToEnglish()
    {
        var result = StyleArtPresetTranslations.Localize("Vintage", "Retro tones", "[\"Posters\"]", "{not-json", "vi");

        result.Name.Should().Be("Vintage");
        result.Description.Should().Be("Retro tones");
        result.Recommendations.Should().Be("[\"Posters\"]");
    }

    [TestMethod]
    public void Localize_WhenCultureIsNotVi_ReturnsEnglish()
    {
        var translations = "{\"vi\":{\"name\":\"Co dien\",\"description\":\"Tong co\",\"recommendations\":[\"Ap phich\"]}}";

        var result = StyleArtPresetTranslations.Localize("Vintage", "Retro tones", "[\"Posters\"]", translations, "en");

        result.Name.Should().Be("Vintage");
        result.Description.Should().Be("Retro tones");
        result.Recommendations.Should().Be("[\"Posters\"]");
    }

    [TestMethod]
    public void ParseState_WhenMetadataPresent_ReturnsPendingWithItsSchedule()
    {
        var state = StyleArtPresetTranslations.ParseState(
            "{\"vi\":{\"name\":\"Co dien\"},\"status\":\"pending\",\"attempts\":2,\"nextRetryAt\":\"2026-11-14T05:00:00.0000000Z\"}");

        state.Should().NotBeNull();
        state!.Status.Should().Be("pending");
        state.Attempts.Should().Be(2);
        state.NextRetryAt.Should().NotBeNull();
    }

    [TestMethod]
    public void ParseState_WhenNextRetryAtInvalid_KeepsOtherFields()
    {
        var state = StyleArtPresetTranslations.ParseState(
            "{\"vi\":{\"name\":\"Co dien\"},\"status\":\"pending\",\"attempts\":1,\"nextRetryAt\":\"not-a-date\"}");

        state.Should().NotBeNull();
        state!.NextRetryAt.Should().BeNull();
        state.Status.Should().Be("pending");
    }

    [TestMethod]
    public void ParseState_WhenMissingOrInvalid_ReturnsNull()
    {
        StyleArtPresetTranslations.ParseState(null).Should().BeNull();
        StyleArtPresetTranslations.ParseState("{not-json").Should().BeNull();
    }

    [TestMethod]
    public void SerializeDocument_RoundTripsThroughLocalizeAndParseState()
    {
        var vi = new StyleArtPresetTranslations.ViTranslation("Co dien", "Tong co", ["Ap phich"]);
        var nextRetry = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var document = StyleArtPresetTranslations.SerializeDocument(vi, status: "pending", attempts: 1, nextRetryAt: nextRetry);

        var localized = StyleArtPresetTranslations.Localize("Vintage", "Retro tones", "[\"Posters\"]", document, "vi");
        localized.Name.Should().Be("Co dien");
        localized.Description.Should().Be("Tong co");

        var state = StyleArtPresetTranslations.ParseState(document);
        state!.Status.Should().Be("pending");
        state.Attempts.Should().Be(1);
        state.NextRetryAt.Should().Be(nextRetry);

        using var parsed = JsonDocument.Parse(document);
        parsed.RootElement.GetProperty("vi").GetProperty("recommendations").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("Ap phich");
    }

    [TestMethod]
    public void Queued_IsDueAtOnce()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        var state = StyleArtPresetTranslations.ParseState(StyleArtPresetTranslations.Queued(now));

        state!.Status.Should().Be(StyleArtPresetTranslations.Pending);
        state.Attempts.Should().Be(0);
        StyleArtPresetTranslations.IsDue(state, now).Should().BeTrue();
    }

    [TestMethod]
    public void AfterFailedAttempt_RetriesAfterFiveThenTenMinutesThenGivesUp()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var queued = StyleArtPresetTranslations.ParseState(StyleArtPresetTranslations.Queued(now))!;

        var first = StyleArtPresetTranslations.ParseState(StyleArtPresetTranslations.AfterFailedAttempt(queued, now))!;
        first.Status.Should().Be(StyleArtPresetTranslations.Pending);
        first.NextRetryAt.Should().Be(now.AddMinutes(5));
        StyleArtPresetTranslations.IsDue(first, now.AddMinutes(4)).Should().BeFalse();

        var second = StyleArtPresetTranslations.ParseState(StyleArtPresetTranslations.AfterFailedAttempt(first, now))!;
        second.Status.Should().Be(StyleArtPresetTranslations.Pending);
        second.NextRetryAt.Should().Be(now.AddMinutes(10));

        var third = StyleArtPresetTranslations.ParseState(StyleArtPresetTranslations.AfterFailedAttempt(second, now))!;
        third.Status.Should().Be(StyleArtPresetTranslations.Failed);
        third.NextRetryAt.Should().BeNull();
        StyleArtPresetTranslations.IsDue(third, now.AddDays(1)).Should().BeFalse();
    }

    [TestMethod]
    public void Succeeded_StoresTheTranslationAndClearsTheSchedule()
    {
        var document = StyleArtPresetTranslations.Succeeded(new StyleArtPresetTranslations.ViTranslation("Co dien", "Tong co", []));

        var state = StyleArtPresetTranslations.ParseState(document)!;
        state.Status.Should().Be(StyleArtPresetTranslations.Translated);
        state.NextRetryAt.Should().BeNull();
        StyleArtPresetTranslations.ParseViDocument(document)!.Name.Should().Be("Co dien");
    }
}
