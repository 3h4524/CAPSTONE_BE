using APCS.Application.Abstractions.Moderation;
using APCS.Application.Features.Moderation.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Moderation;

[TestClass]
public sealed class BlocklistMatcherTests
{
    private static readonly ModerationTermEntry[] Terms =
    [
        new("porn", ModerationCategories.Sexual, "block", "word", false),
        new("nude", ModerationCategories.Sexual, "flag", "word", false),
        new("sex", ModerationCategories.Sexual, "flag", "word", false),
        new("child porn", ModerationCategories.SexualMinors, "block", "phrase", false),
        new("lồn", ModerationCategories.Sexual, "block", "word", false),
        new("nứng", ModerationCategories.Sexual, "block", "word", false),
        new("đụ", ModerationCategories.Sexual, "block", "word", false),
        new("địt", ModerationCategories.Sexual, "block", "word", true),
        new("ấu dâm", ModerationCategories.SexualMinors, "block", "phrase", true)
    ];

    private static readonly string[] Allowlist = ["naked eye", "nude color", "sex education"];

    private readonly BlocklistMatcher matcher = new(Terms, Allowlist);

    [TestMethod]
    [DataRow("retro porn poster")]
    [DataRow("P0RN poster")]
    [DataRow("p.o.r.n poster")]
    [DataRow("p o r n poster")]
    [DataRow("pornnn")]
    public void Match_WaysOfWritingRoundTheList_StillFindTheTerm(string text)
    {
        matcher.Match(text).Should().ContainSingle(hit => hit.Term == "porn" && hit.Blocks);
    }

    [TestMethod]
    [DataRow("Essex county vintage tee")]
    [DataRow("Sussex rugby club")]
    [DataRow("pornography")]
    [DataRow("a cozy sweater")]
    public void Match_TermInsideAnotherWord_IsNotFound(string text)
    {
        matcher.Match(text).Should().BeEmpty();
    }

    [TestMethod]
    public void Match_PhraseSplitByPunctuation_IsFound()
    {
        matcher.Match("child-porn").Should().ContainSingle(hit => hit.Category == ModerationCategories.SexualMinors);
    }

    [TestMethod]
    public void Match_AllowlistedPhrase_IsMasked()
    {
        matcher.Match("A shirt in a nude color, seen with the naked eye").Should().BeEmpty();
        matcher.Match("sex education poster").Should().BeEmpty();
        matcher.Match("nude figure drawing").Should().ContainSingle(hit => hit.Term == "nude" && !hit.Blocks);
    }

    [TestMethod]
    [DataRow("áo in chữ lồn", "lồn")]
    [DataRow("Nứng", "nứng")]
    [DataRow("NỨNG QUÁ", "nứng")]
    [DataRow("đụ má", "đụ")]
    public void Match_VietnameseTerm_IsMatchedWithItsAccents(string text, string term)
    {
        matcher.Match(text).Should().ContainSingle(hit => hit.Term == term && hit.Blocks);
    }

    [TestMethod]
    [DataRow("lon bia mát lạnh")]
    [DataRow("các mẫu áo đẹp")]
    [DataRow("du lịch Đà Lạt")]
    [DataRow("lò nung gạch thủ công")]
    [DataRow("nung nấu ước mơ")]
    public void Match_AccentFreeSpellingOfATermMatchedWithAccentsOnly_IsNotFound(string text)
    {
        matcher.Match(text).Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("dit me")]
    [DataRow("au dam")]
    public void Match_TermMarkedForAccentFreeMatching_IsFoundWithoutAccents(string text)
    {
        matcher.Match(text).Should().ContainSingle(hit => hit.Blocks);
    }

    [TestMethod]
    public void Match_EmptyListsOrText_FindNothing()
    {
        BlocklistMatcher.Empty.Match("porn").Should().BeEmpty();
        matcher.Match(null).Should().BeEmpty();
        matcher.Match("   ").Should().BeEmpty();
    }
}
