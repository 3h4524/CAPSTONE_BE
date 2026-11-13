using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketConceptMatcherTests
{
    private static MarketConceptMatcher.Concept Concept(string title, params string[] tags) => new(Guid.NewGuid(), title, tags);

    [TestMethod]
    public void Match_ListingsSharingThreeDistinctiveTags_AreOneConcept_ThoughTheirTitlesDiffer()
    {
        var pun = Concept("Comfort Colors Funny Math Teacher Shirt, Be Like Subtraction", "math pun shirt", "pi day shirt", "algebra teacher", "nerdy gift");
        var other = Concept("Retro Math Teacher Sweatshirt", "retro teacher", "math pun shirt");

        var match = MarketConceptMatcher.Match("Funny Math Teacher Shirt, Do Not Divide",
            new HashSet<string> { "math pun shirt", "pi day shirt", "algebra teacher", "teacher gift" }, [other, pun]);

        match.Should().BeSameAs(pun);
    }

    [TestMethod]
    public void Match_TwoSharedTags_OrADifferentTitle_IsAnotherConcept()
    {
        var concept = Concept("Grumpy Cat Coffee Shirt", "grumpy cat", "coffee lover");

        MarketConceptMatcher.Match("Space cat galaxy tee", new HashSet<string> { "grumpy cat", "coffee lover", "space" }, [concept]).Should().BeNull();
        MarketConceptMatcher.Match("Grumpy Cat Coffee Shirt Funny", new HashSet<string>(), [concept]).Should().BeSameAs(concept, "titles nearly the same still match");
    }

    [TestMethod]
    public void CommonTags_TheNichePhraseAndTagsOnManyListings_DoNotTellConceptsApart()
    {
        var listings = Enumerable.Range(0, 20)
            .Select(index => (IEnumerable<string>)(index < 4 ? ["Teacher  Shirt", "back to school", $"rare {index}"] : ["back to school", $"rare {index}"]))
            .ToArray();

        var common = MarketConceptMatcher.CommonTags("Teacher Shirt", listings);

        common.Should().BeEquivalentTo(["teacher shirt", "back to school"], "4 of 20 listings is 20%, past the 15% share");
        MarketConceptMatcher.DistinctiveTags(["Back To School", "rare 3", "Math Pun"], common).Should().BeEquivalentTo(["rare 3", "math pun"]);
    }

    [TestMethod]
    public void CommonTags_TooFewListings_OnlyLeaveOutTheNiche()
    {
        MarketConceptMatcher.CommonTags("cat shirt", [["cat shirt", "grumpy"], ["grumpy"]]).Should().BeEquivalentTo(["cat shirt"]);
    }
}
