using APCS.Application.Abstractions.AI;
using APCS.Application.Features.DesignGeneration.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.DesignGeneration;

[TestClass]
public class DesignTextCheckTests
{
    private static IReadOnlyList<DesignTextLine> Lines(params string[] texts) =>
        texts.Select(text => new DesignTextLine(text, 0.9)).ToList();

    [TestMethod]
    public void Compare_PhraseReadOnSeveralLines_IgnoringCaseAndPunctuation_Passes()
    {
        var result = DesignTextCheck.Compare("Coffee, then Code!", Lines("COFFEE", "THEN", "code"));

        result.Ok.Should().BeTrue();
        result.Missing.Should().BeEmpty();
        result.Extra.Should().BeEmpty();
        result.CharacterErrorRate.Should().Be(0);
        result.Read.Should().Be("COFFEE / THEN / code");
    }

    [TestMethod]
    public void Compare_WordsGluedTogetherByOcr_StillCountAsRead()
    {
        var result = DesignTextCheck.Compare("Vintage Fishing Club", Lines("VINTAGE", "FISHINGCLUB"));

        result.Ok.Should().BeTrue();
        result.Extra.Should().BeEmpty();
    }

    [TestMethod]
    public void Compare_ExtraWords_Fail()
    {
        var result = DesignTextCheck.Compare("Vintage Camping Adventure", Lines("VINTAGE CAMPING ADVENTURE", "EST. WILD & FREE"));

        result.Ok.Should().BeFalse();
        result.Extra.Should().Equal("EST", "WILD", "FREE");
    }

    [TestMethod]
    public void Compare_MisspeltWord_FailsAsMissingAndExtra_WithItsCharacterErrorRate()
    {
        var result = DesignTextCheck.Compare("Retro Sunset Surf", Lines("RETRO SUNSFT SURF"));

        result.Ok.Should().BeFalse();
        result.Missing.Should().Equal("SUNSET");
        result.Extra.Should().Equal("SUNSFT");
        result.CharacterErrorRate.Should().BeApproximately(1.0 / 15, 1e-4);
    }

    [TestMethod]
    public void Compare_RepeatedWord_Fails()
    {
        var result = DesignTextCheck.Compare("Sunset Surf", Lines("SUNSET", "SUNSET SURF"));

        result.Ok.Should().BeFalse();
        result.Repeated.Should().Equal("SUNSET");
    }

    [TestMethod]
    public void Compare_LowConfidenceLinesAndSingleCharacters_AreIgnored()
    {
        var lines = new List<DesignTextLine>
        {
            new("GAME DAY", 0.97), new("XQZT", 0.3), new("×", 0.9), new("A", 0.8)
        };

        var result = DesignTextCheck.Compare("Game Day", lines);

        result.Ok.Should().BeTrue();
        result.Read.Should().Be("GAME DAY / × / A");
    }

    [TestMethod]
    public void Compare_NoPhrase_PassesOnlyWithoutWords()
    {
        DesignTextCheck.Compare(null, Lines()).Ok.Should().BeTrue();
        DesignTextCheck.Compare(null, Lines()).CharacterErrorRate.Should().BeNull();

        var withWords = DesignTextCheck.Compare(" ", Lines("Brand"));
        withWords.Ok.Should().BeFalse();
        withWords.Expected.Should().BeNull();
        withWords.Extra.Should().Equal("BRAND");
    }

    [TestMethod]
    public void Compare_LettersWithAccents_AreKeptInWords()
    {
        DesignTextCheck.Compare("Cà phê sữa", Lines("CÀ PHÊ SỮA")).Ok.Should().BeTrue();
    }

    [TestMethod]
    public void Metadata_RoundTrips()
    {
        var result = DesignTextCheck.Compare("Retro Sunset Surf", Lines("RETRO SUNSFT SURF", "EST"));
        var metadata = new System.Text.Json.Nodes.JsonObject
        {
            ["promptVersion"] = "v3",
            [DesignTextCheck.MetadataKey] = DesignTextCheck.ToJson(result)
        }.ToJsonString();

        var read = DesignTextCheck.FromMetadata(metadata);

        read.Should().BeEquivalentTo(result);
    }

    [TestMethod]
    public void FromMetadata_WithoutACheck_OrUnreadable_IsNull()
    {
        DesignTextCheck.FromMetadata(null).Should().BeNull();
        DesignTextCheck.FromMetadata("""{"promptVersion": "v3"}""").Should().BeNull();
        DesignTextCheck.FromMetadata("not json").Should().BeNull();
        DesignTextCheck.FromMetadata("""{"textCheck": {"read": "x"}}""").Should().BeNull();
    }
}
