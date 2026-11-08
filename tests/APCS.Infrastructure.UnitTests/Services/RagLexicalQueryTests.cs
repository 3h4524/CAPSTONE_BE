using APCS.Infrastructure.Services;
using FluentAssertions;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class RagLexicalQueryTests
{
    [TestMethod]
    public void BuildAnyTermTsQuery_JoinsTermsWithOr()
    {
        RagService.BuildAnyTermTsQuery(["Embroidered", "sweatshirt", "teacher"])
            .Should().Be("embroidered | sweatshirt | teacher");
    }

    [TestMethod]
    public void BuildAnyTermTsQuery_DropsStopWordsAndShortTerms()
    {
        RagService.BuildAnyTermTsQuery(["gift", "for", "the", "a", "mom"])
            .Should().Be("gift | mom");
    }

    [TestMethod]
    public void BuildAnyTermTsQuery_SplitsHyphenatedTermsIntoLexemes()
    {
        RagService.BuildAnyTermTsQuery(["t-shirt", "mother's"])
            .Should().Be("shirt | mother");
    }

    [TestMethod]
    public void BuildAnyTermTsQuery_StripsTsQueryOperators()
    {
        // Operators and quotes must not reach to_tsquery, or a user query could break the search.
        RagService.BuildAnyTermTsQuery(["shirt&!dress", "(hat)", "cap:*", "'quote'"])
            .Should().Be("shirt | dress | hat | cap | quote");
    }

    [TestMethod]
    public void BuildAnyTermTsQuery_KeepsVietnameseLetters()
    {
        RagService.BuildAnyTermTsQuery(["áo", "thêu"]).Should().Be("áo | thêu");
    }

    [TestMethod]
    public void BuildAnyTermTsQuery_ReturnsNullWhenNothingIsLeft()
    {
        RagService.BuildAnyTermTsQuery(["the", "&", "a"]).Should().BeNull();
    }
}
