using APCS.Application.Features.BatchProductPrompts.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchProductPrompts;

[TestClass]
public sealed class PromptComposerTests
{
    private static string Compose(string basePrompt, string keywords = "", string description = "", string styleModifiers = "", string negative = "") =>
        PromptComposer.Compose(basePrompt, "Mountain Sunrise", "Watercolor", "", negative, "", "hiking", styleModifiers, keywords, description);

    [TestMethod]
    public void Compose_ReplacesKeywordsPlaceholder()
    {
        var prompt = Compose("Art of {subject} using {keywords}.", keywords: "mountain, hiking");

        prompt.Should().Contain("using mountain, hiking").And.NotContain("{keywords}");
    }

    [TestMethod]
    public void Compose_AppendsKeywordsWhenTemplateDoesNotUsePlaceholder()
    {
        var prompt = Compose("Art of {subject}.", keywords: "mountain, hiking");

        prompt.Should().Contain("keywords: mountain, hiking");
    }

    [TestMethod]
    public void Compose_AcceptsDoubleBracePlaceholders()
    {
        var prompt = Compose("A {{style}} picture of {{subject}} for {{niche}}");

        prompt.Should().Be("A Watercolor picture of Mountain Sunrise for hiking");
    }

    [TestMethod]
    public void Compose_AppendsDescription_AndTruncatesLongOnes()
    {
        Compose("Art of {subject}", description: "calm colors").Should().Contain("Additional guidance: calm colors");

        var longPrompt = Compose("Art of {subject}", description: new string('x', 900));
        longPrompt.Length.Should().BeLessThan(PromptComposer.MaximumDescriptionLength + 100);
    }

    [TestMethod]
    public void Compose_DoesNotLeaveDoublePunctuationBeforeStyleModifiers()
    {
        var prompt = Compose("Art of {subject}. No mockup.", styleModifiers: "watercolor painting");

        prompt.Should().Contain("No mockup, Watercolor, watercolor painting").And.NotContain(".,");
    }

    [TestMethod]
    public void Compose_WithoutNewInputs_KeepsPreviousBehaviour()
    {
        var prompt = PromptComposer.Compose("Art of {subject} in {style}", "Cat", "Vintage", "", "blurry", "", "pets", "grain");

        prompt.Should().Be("Art of Cat in Vintage, grain\nNegative prompt: blurry");
    }
}
