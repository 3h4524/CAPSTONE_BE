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
    public void ParseState_WhenMetadataPresent_ReturnsManualAndPending()
    {
        var state = StyleArtPresetTranslations.ParseState(
            "{\"vi\":{\"name\":\"Co dien\"},\"manual\":true,\"status\":\"pending\",\"attempts\":2,\"nextRetryAt\":\"2026-11-14T05:00:00.0000000Z\"}");

        state.Should().NotBeNull();
        state!.Manual.Should().BeTrue();
        state.Status.Should().Be("pending");
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

        var document = StyleArtPresetTranslations.SerializeDocument(vi, manual: false, status: "pending", attempts: 1, nextRetryAt: nextRetry);

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
}
