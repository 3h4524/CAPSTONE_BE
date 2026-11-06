using APCS.Application.Features.Rag;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Rag;

[TestClass]
public sealed class RagKnownItemCaseBuilderTests
{
    private static RagKnownItemSource Listing(string id, string niche = "bridal sweatshirt", string tags = "Bridal Party, Bride Gift, Bridesmaid Gift, Engagement", string description = "Say yes to the cutest pre-wedding sweatshirt.") =>
        new(id, niche, $"Public marketplace listing reference from Etsy.\nTitle: {id}\nDescription: {description}\nTags: {tags}\nPrice: 30 USD\n");

    [TestMethod]
    public void Build_WritesAShortAndALongQueryWhoseExpectedAnswerIsTheSourceListing()
    {
        var cases = RagKnownItemCaseBuilder.Build([Listing("etsy:public-listing:1")], 5, seed: 7);

        cases.Should().HaveCount(2);
        cases.Should().OnlyContain(item => item.ExpectedSourceId == "etsy:public-listing:1" && item.ContentType == "listing");
        cases[0].Category.Should().Be(RagKnownItemCaseBuilder.ShortCategory);
        cases[0].Search.Query.Should().Be("bridal sweatshirt Bridal Party Bride Gift Bridesmaid Gift");
        cases[1].Category.Should().Be(RagKnownItemCaseBuilder.LongCategory);
        cases[1].Search.Query.Should().Be("bridal sweatshirt; Bridal Party, Bride Gift, Bridesmaid Gift, Engagement; Say yes to the cutest pre-wedding sweatshirt.");
        cases.Should().OnlyContain(item => item.Search.TopK == RagKnownItemCaseBuilder.TopK && item.Search.ContentType == "listing");
    }

    [TestMethod]
    public void Build_SamplesTheSameListingsForTheSameSeed()
    {
        var sources = Enumerable.Range(1, 50).Select(index => Listing($"etsy:public-listing:{index}")).ToArray();

        var first = RagKnownItemCaseBuilder.Build(sources, 10, seed: 42).Select(item => item.Id);
        var second = RagKnownItemCaseBuilder.Build(sources.Reverse(), 10, seed: 42).Select(item => item.Id);
        var other = RagKnownItemCaseBuilder.Build(sources, 10, seed: 43).Select(item => item.Id);

        first.Should().HaveCount(20).And.Equal(second);
        other.Should().NotEqual(first);
    }

    [TestMethod]
    public void Build_SkipsListingsWithoutTagsOrDescriptionAndDuplicateSources()
    {
        var sources = new[]
        {
            Listing("etsy:public-listing:1"),
            Listing("etsy:public-listing:1"),
            new RagKnownItemSource("etsy:public-listing:2", "mom shirt", "Title: only a title\n")
        };

        var cases = RagKnownItemCaseBuilder.Build(sources, 10, seed: 1);

        cases.Select(item => item.ExpectedSourceId).Distinct().Should().Equal("etsy:public-listing:1");
    }

    [TestMethod]
    public void Build_CutsTheLongQueryDescriptionTo200Characters()
    {
        var cases = RagKnownItemCaseBuilder.Build([Listing("etsy:public-listing:1", description: new string('x', 500))], 1, seed: 1);

        cases.Single(item => item.Category == RagKnownItemCaseBuilder.LongCategory).Search.Query
            .Should().EndWith("; " + new string('x', 200));
    }
}
