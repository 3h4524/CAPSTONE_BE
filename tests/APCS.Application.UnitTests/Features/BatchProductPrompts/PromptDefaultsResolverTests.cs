using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.BatchProductPrompts;

[TestClass]
public sealed class PromptDefaultsResolverTests
{
    private static readonly DesignTemplate PhraseTemplate = new()
    {
        Id = Guid.NewGuid(), Name = "Bold Rally", ArtStyle = "bold_typography",
        BasePrompt = "Create a print-ready {style} composition using the exact phrase \"{subject}\" for the {niche} niche."
    };

    private static readonly DesignTemplate PictureTemplate = new()
    {
        Id = Guid.NewGuid(), Name = "Blue Garden", ArtStyle = "watercolor", BasePrompt = "Paint a {style} arrangement of {subject}."
    };

    private static readonly StyleArtPreset Minimalist = new()
    {
        Id = Guid.NewGuid(), Name = "Minimalist", IsActive = true,
        StyleModifiers = "minimalist composition, clean lines, ample negative space, soft neutral tones"
    };

    private static Task<PromptComponents> ResolveAsync(Product product, DesignTemplate template)
    {
        product.DesignTemplateId = template.Id;
        var templates = new Mock<IRepository<DesignTemplate>>();
        templates.Setup(x => x.Query()).Returns(new[] { PhraseTemplate, PictureTemplate }.AsQueryable().BuildMock());
        var styles = new Mock<IRepository<StyleArtPreset>>();
        styles.Setup(x => x.Query()).Returns(new[] { Minimalist }.AsQueryable().BuildMock());
        return PromptDefaultsResolver.ResolveAsync(product, templates.Object, styles.Object, CancellationToken.None);
    }

    private static Product Product(string? designText = null, string? keywords = "mountain, hiking shirt", string? niche = "Hikers") => new()
    {
        Id = Guid.NewGuid(), Name = "Minimalist Mountain Sunrise T-Shirt", ProductType = "tshirt",
        DesiredDesignText = designText, MainKeywords = keywords, NicheCategory = niche
    };

    [TestMethod]
    public async Task ResolveAsync_PhraseTemplateWithDesignText_PrintsTheDesignText()
    {
        var components = await ResolveAsync(Product(designText: "  GO HIGHER "), PhraseTemplate);

        components.Subject.Should().Be("GO HIGHER");
    }

    [TestMethod]
    public async Task ResolveAsync_PhraseTemplateWithoutDesignText_PrintsTheProductName()
    {
        var components = await ResolveAsync(Product(), PhraseTemplate);

        components.Subject.Should().Be("Minimalist Mountain Sunrise");
    }

    [TestMethod]
    public async Task ResolveAsync_PictureTemplate_DrawsTheProductNameEvenWithDesignText()
    {
        var components = await ResolveAsync(Product(designText: "GO HIGHER"), PictureTemplate);

        components.Subject.Should().Be("Minimalist Mountain Sunrise");
    }

    [TestMethod]
    public async Task ResolveAsync_WithoutAStylePreset_UsesTheTemplatesArtStyle()
    {
        var components = await ResolveAsync(Product(), PhraseTemplate);

        components.ArtStyle.Should().Be("bold typography");
        PromptComposer.Compose(components.BasePrompt, components.Subject, components.ArtStyle, "", "", "", components.Niche, "")
            .Should().StartWith("Create a print-ready bold typography composition");
    }

    [TestMethod]
    public async Task ResolveAsync_KeywordsBecomeDrawableMotifs_AndStandInForAMissingNiche()
    {
        var components = await ResolveAsync(Product(keywords: "mountain, hiking shirt, camping gift", niche: null), PictureTemplate);

        components.Keywords.Should().Be("mountain, hiking, camping");
        components.Niche.Should().Be("mountain, hiking, camping");
    }

    [TestMethod]
    public async Task ResolveAsync_CustomPrompt_ReplacesTheTemplate_AndPrintsTheDesignTextAsItsPhrase()
    {
        var product = Product(designText: "GO HIGHER", keywords: "mountain, hiking shirt");
        product.CustomPrompt = "  A bold mountain badge at sunrise.  ";
        product.InputDescription = "A sunrise over the peaks with pine trees.";

        var components = await ResolveAsync(product, PictureTemplate);

        components.BasePrompt.Should().Be("A bold mountain badge at sunrise. " + PromptDefaultsResolver.CustomPhraseSentence);
        components.Subject.Should().Be("GO HIGHER");
        components.Keywords.Should().BeEmpty("the Seller's prompt replaces the keywords");
        components.Description.Should().BeEmpty("the Seller's prompt replaces the description");
        components.MoodTone.Should().BeEmpty();
        ArtworkPromptRules.PrintsExactPhrase(components.BasePrompt).Should().BeTrue();
        PromptComposer.Compose(components.BasePrompt, components.Subject, components.ArtStyle, components.MoodTone, "", "",
                components.Niche, components.StyleModifiers, components.Keywords, components.Description)
            .Should().Be("A bold mountain badge at sunrise. The design prints the exact phrase \"GO HIGHER\".");
    }

    [TestMethod]
    public async Task ResolveAsync_CustomPromptWithoutDesignText_PrintsNoWords()
    {
        var product = Product(keywords: "mountain");
        product.CustomPrompt = "A bold mountain badge at sunrise.";

        var components = await ResolveAsync(product, PhraseTemplate);

        components.BasePrompt.Should().Be("A bold mountain badge at sunrise.");
        components.Subject.Should().BeEmpty("the product name is not printed");
        ArtworkPromptRules.PrintsExactPhrase(components.BasePrompt).Should().BeFalse();
    }

    [TestMethod]
    public async Task ResolveAsync_StylePreset_AddsItsModifiersToATemplatePrompt()
    {
        var product = Product();
        product.StylePreset = "Minimalist";

        var components = await ResolveAsync(product, PictureTemplate);

        components.ArtStyle.Should().Be("Minimalist");
        components.StyleModifiers.Should().Be(Minimalist.StyleModifiers);
        components.OwnPrompt.Should().BeFalse();
    }

    [TestMethod]
    public async Task ResolveAsync_CustomPrompt_TakesNoStylePreset_ItSetsTheLookItself()
    {
        var product = Product();
        product.StylePreset = "Minimalist";
        product.CustomPrompt = "A red and white lighthouse on a rocky coast under a stormy sky.";

        var components = await ResolveAsync(product, PictureTemplate);

        components.ArtStyle.Should().BeEmpty();
        components.StyleModifiers.Should().BeEmpty();
        components.OwnPrompt.Should().BeTrue();
        PromptComposer.Compose(components.BasePrompt, components.Subject, components.ArtStyle, components.MoodTone, "", "",
                components.Niche, components.StyleModifiers, components.Keywords, components.Description)
            .Should().Be("A red and white lighthouse on a rocky coast under a stormy sky.");
    }

    [TestMethod]
    public async Task ResolveAsync_CustomPrompt_QuotesOnlyTheDesignText()
    {
        var product = Product(designText: "Salt Life");
        product.CustomPrompt = "A surf van with \"Salt Life\" on top and \"EST 1985\" underneath.";

        var components = await ResolveAsync(product, PictureTemplate);

        components.BasePrompt.Should().Be("A surf van with \"Salt Life\" on top. " + PromptDefaultsResolver.CustomPhraseSentence);
    }

    [TestMethod]
    public async Task ResolveAsync_KeywordsAboutTheFeel_BecomeTheDefaultMood()
    {
        var components = await ResolveAsync(Product(keywords: "funny camping, vintage hiking shirt, dog mom"), PictureTemplate);

        components.Keywords.Should().Be("camping, hiking, dog");
        components.MoodTone.Should().Be("playful, vintage feel");
    }
}
