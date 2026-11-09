using APCS.Application.Features.ListingContent;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.ListingContent;

[TestClass]
public sealed class ListingSeoScorerTests
{
    private const string GoodTitle = "Mountain Sunset Shirt, Hiking Gift for Dad, Outdoor Graphic Tee";

    private static readonly string[] GoodTags =
    [
        "mountain shirt", "hiking gift", "gift for dad", "outdoor lover tee", "camping shirt", "nature lover gift",
        "fathers day gift", "adventure tee", "sunset graphic tee", "trail runner gift", "national park tee", "retro mountain",
        "hiker birthday",
    ];

    private static readonly string[] Keywords = ["mountain shirt", "hiking gift"];

    private static string GoodDescription() =>
        "This mountain shirt brings a calm sunset over the peaks to every trail day.\n\n"
        + string.Join(' ', Enumerable.Repeat("A soft everyday tee that pairs with jeans or shorts and feels easy on long drives.", 6))
        + "\n\nIt makes a thoughtful hiking gift for the dad who plans every weekend around the outdoors.";

    [TestMethod]
    public void Evaluate_WellBuiltListing_IsKept()
    {
        var result = ListingSeoScorer.Evaluate(GoodTitle, GoodDescription(), GoodTags, Keywords);

        result.Recommendation.Should().Be("keep");
        result.ShouldRegenerate.Should().BeFalse();
        result.Title.Should().Be(100);
        result.KeywordOptimization.Should().Be(100);
        result.KeywordDensity.Should().BeLessThanOrEqualTo(ListingSeoScorer.MaxKeywordDensity);
    }

    [TestMethod]
    public void Evaluate_WithoutProductKeywords_GivesNoFreeKeywordPoints()
    {
        var result = ListingSeoScorer.Evaluate(GoodTitle, GoodDescription(), GoodTags, []);

        result.KeywordOptimization.Should().Be(0);
        result.TagRelevance.Should().Be(0);
        result.Suggestions.Should().Contain(s => s.Contains("Add main keywords"));
    }

    [TestMethod]
    public void Evaluate_KeywordOnlyAsScatteredSingleWord_DoesNotCountAsInTheTitle()
    {
        // "shirt" alone used to satisfy the keyword "mountain shirt".
        var result = ListingSeoScorer.Evaluate("Cozy Cotton Shirt For Weekend Walks And Lazy Afternoons", GoodDescription(), GoodTags, Keywords);

        result.Title.Should().BeLessThan(70);
        result.Suggestions.Should().Contain(s => s.Contains("Put the main keyword \"mountain shirt\" in the title"));
    }

    [TestMethod]
    public void Evaluate_MainKeywordAtTheEndOfTheTitle_LosesTheLeadPoints()
    {
        var late = ListingSeoScorer.Evaluate("Soft Graphic Tee for Dad Who Loves Long Weekend Walks, Mountain Shirt", GoodDescription(), GoodTags, Keywords);

        late.Title.Should().Be(85);
        late.Suggestions.Should().Contain(s => s.StartsWith("Start the title with the main keyword"));
    }

    [TestMethod]
    public void Evaluate_KeywordInPluralForm_StillCovered()
    {
        ListingSeoScorer.Covers("Matching mountain shirts for the family", "mountain shirt").Should().BeTrue();
        ListingSeoScorer.Covers("A shirt with a mountain on it", "mountain shirt").Should().BeTrue();
        ListingSeoScorer.Covers("A plain shirt", "mountain shirt").Should().BeFalse();
    }

    [TestMethod]
    public void Evaluate_StuffedTitle_IsMarkedForRegeneration()
    {
        var stuffed = "Mountain Shirt Mountain Tee Mountain Gift Mountain Hiking Mountain Lover Mountain Dad Mountain Camping Shirt Mountain";

        var result = ListingSeoScorer.Evaluate(stuffed, GoodDescription(), GoodTags, Keywords);

        result.Suggestions.Should().Contain(s => s.StartsWith("Do not repeat words in the title"));
        result.Title.Should().BeLessThan(80);
    }

    [TestMethod]
    public void Evaluate_SingleWordTagsThatRepeatTheTitle_ScoreLow()
    {
        string[] weak = ["mountain", "sunset", "shirt", "hiking", "gift", "dad", "outdoor", "graphic", "tee", "mountain shirt", "hiking gift", "gift dad", "sunset shirt"];

        var result = ListingSeoScorer.Evaluate(GoodTitle, GoodDescription(), weak, Keywords);

        result.Tags.Should().BeLessThan(60);
        result.ShouldRegenerate.Should().BeTrue();
        result.Suggestions.Should().Contain(s => s.StartsWith("Use multi-word tags"));
        result.Suggestions.Should().Contain(s => s.StartsWith("Use tags for searches the title does not cover"));
    }

    [TestMethod]
    public void Evaluate_OneWordInMostTags_AsksForVariety()
    {
        var samey = GoodTags.Select((tag, i) => i < 6 ? $"gift {i + 1} idea" : tag).ToArray();

        var result = ListingSeoScorer.Evaluate(GoodTitle, GoodDescription(), samey, Keywords);

        result.Suggestions.Should().Contain(s => s.StartsWith("Vary the tags: \"gift\""));
    }

    [TestMethod]
    public void Evaluate_KeywordDensity_IsAShareOfDescriptionWords()
    {
        var stuffed = string.Join(' ', Enumerable.Repeat("mountain shirt hiking gift", 10));

        var result = ListingSeoScorer.Evaluate(GoodTitle, stuffed, GoodTags, Keywords);

        result.KeywordDensity.Should().Be(100);
        result.Suggestions.Should().Contain(s => s.StartsWith("The keywords make up 100"));
    }

    [TestMethod]
    public void Evaluate_ShortSingleParagraphDescription_AsksForMore()
    {
        var result = ListingSeoScorer.Evaluate(GoodTitle, "A mountain shirt. A hiking gift.", GoodTags, Keywords);

        result.Description.Should().BeLessThan(70);
        result.Suggestions.Should().Contain(s => s.StartsWith("Expand the description"));
        result.Suggestions.Should().Contain(s => s.StartsWith("Split the description"));
    }

    [TestMethod]
    public void Evaluate_TitleEtsyWouldChange_LosesItsRulePoints()
    {
        var result = ListingSeoScorer.Evaluate("Mountain Shirt: Hiking Gift: Outdoor Graphic Tee for Dad", GoodDescription(), GoodTags, Keywords);

        result.Title.Should().Be(85);
        result.Suggestions.Should().Contain(s => s.Contains("once in a title"));
    }
}
