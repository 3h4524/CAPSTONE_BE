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
    public void Build_WhenPolicyContextIsEmpty_UsesDefaultListingStandards()
    {
        var prompt = ListingPromptBuilder.Build(Input(), [], []);

        prompt.Should().Contain("Default marketplace listing standards");
        prompt.Should().Contain("exactly 13 unique tags");
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
