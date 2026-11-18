using APCS.Application.Features.ListingContent;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.Rag.Dtos;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.ListingContent;

[TestClass]
public sealed class ListingPromptBuilderTests
{
    private static ListingContentInput Input() => new(
        Guid.NewGuid(),
        "Mountain sunset shirt",
        "tshirt",
        "hiking",
        "outdoor enthusiasts",
        "A graphic tee with a mountain sunset design.",
        ["mountain shirt", "hiking gift"],
        "Use a calm, outdoorsy tone.");

    [TestMethod]
    public void Build_NamesTheTextPrintedOnTheDesignAndItsArtwork()
    {
        var prompt = ListingPromptBuilder.Build(Input() with { DesignText = "Not all who wander are lost", DesignPrompt = "retro sunset over pine mountains" }, [], []);

        prompt.Should().Contain("Text printed on the design: Not all who wander are lost");
        prompt.Should().Contain("Artwork: retro sunset over pine mountains");
        prompt.Should().Contain("use those exact words in the title");
        prompt.Should().Contain("never as facts about the product");
    }

    [TestMethod]
    public void DescribesSameProduct_TheSameProductAndDesign_IsCurrent()
    {
        var input = Input() with { DesignText = "Wander", DesignPrompt = "sunset" };

        ListingPromptBuilder.DescribesSameProduct(ListingPromptBuilder.Build(input, [], []), input).Should().BeTrue();
    }

    [TestMethod]
    public void DescribesSameProduct_AnotherDesignTextOrKeyword_IsStale()
    {
        var input = Input() with { DesignText = "Wander" };
        var prompt = ListingPromptBuilder.Build(input, [], []);

        ListingPromptBuilder.DescribesSameProduct(prompt, input with { DesignText = "Explore" }).Should().BeFalse();
        ListingPromptBuilder.DescribesSameProduct(prompt, input with { Keywords = ["camping shirt"] }).Should().BeFalse();
    }

    [TestMethod]
    public void DescribesSameProduct_APromptFromBeforeTheDesignLines_IsComparedWithoutThem()
    {
        var input = Input() with { DesignText = "Wander", DesignPrompt = "sunset" };
        var legacy = string.Join('\n', ListingPromptBuilder.Build(input, [], []).Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("Text printed on the design:") && !line.StartsWith("Artwork:")));

        ListingPromptBuilder.DescribesSameProduct(legacy, input).Should().BeTrue();
        ListingPromptBuilder.DescribesSameProduct(legacy, input with { Name = "Ocean shirt" }).Should().BeFalse();
    }

    [TestMethod]
    public void DescribesSameProduct_ATextWithoutAProductBlock_IsStale() =>
        ListingPromptBuilder.DescribesSameProduct("no product here", Input()).Should().BeFalse();

    [TestMethod]
    public void Build_WhenPolicyContextIsEmpty_UsesDefaultListingStandards()
    {
        var prompt = ListingPromptBuilder.Build(Input(), [], []);

        prompt.Should().Contain("Default marketplace listing standards");
        prompt.Should().Contain("Fill all 13 tag slots");
        prompt.Should().Contain("Generate 16 unique tag candidates");
        prompt.Should().Contain("Start with the first keyword phrase");
        prompt.Should().Contain("20 characters or fewer for Etsy");
        prompt.Should().Contain("Never invent materials");
        prompt.Should().Contain("Return JSON only");
        prompt.Should().Contain("Do not include Markdown");
    }

    [TestMethod]
    public void Build_WhenPolicyContextExists_UsesRetrievedPolicyInsteadOfDefaultFallback()
    {
        var policy = new RagSearchResultDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Etsy policy",
            "policy",
            "Use the shop's configured language and avoid unsupported product claims.",
            null,
            0.98,
            new Dictionary<string, object?>());

        var prompt = ListingPromptBuilder.Build(Input(), [policy], []);

        prompt.Should().Contain("Etsy policy");
        prompt.Should().Contain("avoid unsupported product claims");
        prompt.Should().NotContain("Default marketplace listing standards");
    }

    [TestMethod]
    public void Build_WhenListingReferencesExist_AddsThemAsNonAuthoritativeReferences()
    {
        var listing = new RagSearchResultDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Hiking shirt example",
            "listing",
            "A short, outdoorsy description example.",
            null,
            0.91,
            new Dictionary<string, object?>());

        var prompt = ListingPromptBuilder.Build(Input(), [], [listing]);

        prompt.Should().Contain("RELEVANT LISTING REFERENCES");
        prompt.Should().Contain("Hiking shirt example");
        prompt.Should().Contain("Do not copy them");
    }
}
