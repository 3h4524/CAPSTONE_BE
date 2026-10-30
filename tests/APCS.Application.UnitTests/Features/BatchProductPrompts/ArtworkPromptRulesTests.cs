using APCS.Application.Features.BatchProductPrompts.Common;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.BatchProductPrompts;

[TestClass]
public sealed class ArtworkPromptRulesTests
{
    private const string PhraseTemplate =
        "Create a {style} composition using the exact phrase \"{subject}\", use {keywords} as small supporting motifs.";

    private static PromptComponents Components(string subject, string keywords, string description = "", string basePrompt = PhraseTemplate) =>
        new(subject, "bold typography", "", "", "", basePrompt, Keywords: keywords, Description: description);

    [TestMethod]
    public void MotifKeywords_TakesListingWordsOutAndKeepsTheFirstFive()
    {
        var motifs = ArtworkPromptRules.MotifKeywords(
            "vintage camping, hiking shirt, camping gift, unisex apparel, t-shirt, coffee lover, national park, nature, forest");

        motifs.Should().Be("camping, hiking, coffee, national park, nature");
    }

    [TestMethod]
    public void MotifKeywords_DropsRepeatsWhateverTheirCase()
    {
        ArtworkPromptRules.MotifKeywords("Mountain, mountain shirt, MOUNTAIN").Should().Be("Mountain");
    }

    [TestMethod]
    public void MotifKeywords_NothingToDraw_IsEmpty()
    {
        ArtworkPromptRules.MotifKeywords(null).Should().BeEmpty();
        ArtworkPromptRules.MotifKeywords("  ").Should().BeEmpty();
        ArtworkPromptRules.MotifKeywords("gift for her, unisex t shirt").Should().BeEmpty();
    }

    [TestMethod]
    public void SplitKeywords_DropsWordsForAPerson_ButKeepsJobs()
    {
        var parts = ArtworkPromptRules.SplitKeywords("dog mom, best friends, nurse life, developer");

        parts.Motifs.Should().Be("dog, nurse life, developer");
        parts.Mood.Should().BeEmpty("\"best\" names no feel a picture could show");
    }

    [TestMethod]
    public void SplitKeywords_WordsAboutTheFeel_BecomeTheMoodInsteadOfMotifs()
    {
        var parts = ArtworkPromptRules.SplitKeywords("funny cat, cute, vintage, retro camper, cool, hilarious");

        parts.Motifs.Should().Be("cat, camper");
        parts.Mood.Should().Be("playful, cute, vintage, retro feel");
    }

    [TestMethod]
    public void Fit_PhraseTemplate_DropsMotifsThePhraseAlreadySays()
    {
        var fitted = ArtworkPromptRules.Fit(Components("Coffee Then Code", "coffee, coding, codes, laptop"));

        fitted.Keywords.Should().Be("coding, laptop");
    }

    [TestMethod]
    [DataRow("A coffee cup with code brackets and the text Coffee Then Code.", "A coffee cup with code brackets.")]
    [DataRow("A mug with the words \"Coffee Then Code\" in bold.", "A mug in bold.")]
    [DataRow("The text Coffee Then Code on a steaming mug.", "on a steaming mug.")]
    [DataRow("Big lettering of Coffee Then Code above a cup.", "Big lettering of the phrase above a cup.")]
    [DataRow("A cozy desk scene with a laptop.", "A cozy desk scene with a laptop.")]
    public void Fit_PhraseTemplate_TakesTheRepeatedPhraseOutOfTheDescription(string description, string expected)
    {
        ArtworkPromptRules.Fit(Components("Coffee Then Code", "laptop", description)).Description.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("A mug with the words \"Best Dad\" and a coffee cup.", "A mug and a coffee cup.")]
    [DataRow("A mug with \"Coffee Then Code\" and \"EST 2020\" under it.", "A mug.")]
    [DataRow("Sized for a 12\" x 18\" frame.", "Sized for a 12\" x 18\" frame.")]
    public void Fit_PhraseTemplate_LeavesOutQuotedWordsThatAreNotThePhrase(string description, string expected)
    {
        ArtworkPromptRules.Fit(Components("Coffee Then Code", "laptop", description)).Description.Should().Be(expected);
    }

    [TestMethod]
    public void Fit_PictureTemplate_LeavesOutQuotedWords_ItPrintsNoWords()
    {
        var fitted = ArtworkPromptRules.Fit(Components("Fox", "", "A fox under a pine with \"Stay Wild\".", "A watercolor of {subject}."));

        fitted.Description.Should().Be("A fox under a pine.");
    }

    [TestMethod]
    public void Fit_DescriptionThatIsOnlyQuotedWords_IsDropped()
    {
        ArtworkPromptRules.Fit(Components("Coffee Then Code", "laptop", "\"Best Dad\"")).Description.Should().BeEmpty();
    }

    [TestMethod]
    public void Fit_PhraseTemplate_NoMotifLeft_UsesTheStandIn()
    {
        ArtworkPromptRules.Fit(Components("Coffee Then Code", "coffee")).Keywords.Should().Be(ArtworkPromptRules.FallbackMotifs);
    }

    [TestMethod]
    public void Fit_NoMotifLeft_AsksForIconsThatSuitTheNiche_ForTheModelToPick()
    {
        // "gift, funny" leaves nothing to draw once listing words and the feel are taken out.
        var fitted = ArtworkPromptRules.Fit(
            Components("Dink Responsibly", ArtworkPromptRules.MotifKeywords("gift, funny")) with { Niche = "Pickleball players" });

        fitted.Keywords.Should().Be("simple icons that suit Pickleball players");
    }

    [TestMethod]
    public void Fit_NoMotifLeftAndNoNiche_UsesThePlainStandIn()
    {
        ArtworkPromptRules.Fit(Components("Dink Responsibly", "") with { Niche = "general" }).Keywords
            .Should().Be(ArtworkPromptRules.FallbackMotifs);
    }

    [TestMethod]
    public void IsThin_NothingToDraw_AFewWordsOfDescription_AndNoInstructions()
    {
        // "coffee shirt, gift for mom" once listing words and persons are out: only a word the phrase already says.
        var keywords = ArtworkPromptRules.MotifKeywords("coffee shirt, gift for mom");
        ArtworkPromptRules.IsThin(Components("Coffee Then Code", keywords, "Cute design.")).Should().BeTrue();
    }

    [TestMethod]
    public void IsThin_NotWhenTheSellerSaysEnough()
    {
        ArtworkPromptRules.IsThin(Components("Coffee Then Code", "laptop")).Should().BeFalse("there is a motif to draw");
        ArtworkPromptRules.IsThin(Components("Coffee Then Code", "", "A steaming mug on a desk beside a keyboard.")).Should().BeFalse();
        ArtworkPromptRules.IsThin(Components("Coffee Then Code", "") with { Instructions = "Retro diner sign style." }).Should().BeFalse();
    }

    [TestMethod]
    public void Fit_PictureTemplate_LeavesTheMotifsAndDescriptionAlone()
    {
        var fitted = ArtworkPromptRules.Fit(Components("Coffee Then Code", "coffee", "Coffee Then Code on a mug.", "A watercolor of {subject} with {keywords}."));

        fitted.Keywords.Should().Be("coffee");
        fitted.Description.Should().Be("Coffee Then Code on a mug.");
    }

    [TestMethod]
    public void Fit_TemplateThatAsksForNoMotifs_GetsNoStandIn()
    {
        ArtworkPromptRules.Fit(Components("Wildflowers", "", basePrompt: "A watercolor of {subject}.")).Keywords.Should().BeEmpty();
    }

    [TestMethod]
    public void PrintsExactPhrase_OnlyForATemplateThatQuotesTheSubject()
    {
        ArtworkPromptRules.PrintsExactPhrase("Use the exact phrase \"{subject}\" for {niche}.").Should().BeTrue();
        ArtworkPromptRules.PrintsExactPhrase("Use the exact phrase \"{{ subject }}\".").Should().BeTrue();
        ArtworkPromptRules.PrintsExactPhrase("A watercolor of {subject}.").Should().BeFalse();
        ArtworkPromptRules.PrintsExactPhrase(null).Should().BeFalse();
    }

    [TestMethod]
    public void LetteringInstruction_PhraseTemplate_AllowsOnlyThePhrase_InLightLetteringWithADarkOutline()
    {
        var instruction = ArtworkPromptRules.LetteringInstruction("Use the exact phrase \"{subject}\".");

        instruction.Should().Contain("the only text in the artwork is the exact quoted phrase")
            .And.Contain("never as a word")
            .And.Contain("one richly detailed featured illustration in vivid, saturated full color")
            .And.Contain("without covering any letter")
            .And.Contain("sticker-style double outline");
    }

    [TestMethod]
    [DataRow(true, "readable on both light and dark products.")]
    [DataRow(false, "readable against the background.")]
    public void LetteringInstruction_SellersOwnPrompt_KeepsTheWordsRight_ButLeavesTheLookToThem(bool onGarment, string ending)
    {
        var instruction = ArtworkPromptRules.LetteringInstruction("A badge. " + PromptDefaultsResolver.CustomPhraseSentence, onGarment, ownPrompt: true);

        instruction.Should().Contain("the only text in the artwork is the exact quoted phrase")
            .And.Contain("Add no other words")
            .And.Contain("never as a word or label")
            .And.Contain("nothing covering it")
            .And.EndWith(ending);
        instruction.Should().NotContain("featured illustration").And.NotContain("vivid").And.NotContain("outline");
    }

    [TestMethod]
    public void LetteringInstruction_SellersOwnPromptWithoutWords_DrawsMotifsAsPictures()
    {
        ArtworkPromptRules.LetteringInstruction("A tiger head among hibiscus flowers.", ownPrompt: true)
            .Should().Be("Draw every motif as a picture, never as a word or label.");
    }

    [TestMethod]
    public void LetteringInstruction_PhraseOnItsOwnBackdrop_OnlyHasToStandOutFromIt()
    {
        ArtworkPromptRules.LetteringInstruction("Use the exact phrase \"{subject}\".", onGarment: false)
            .Should().Contain("solid colors that contrast strongly with the background")
            .And.NotContain("double outline");
    }

    [TestMethod]
    public void LetteringInstruction_OtherTemplate_DrawsMotifsAsPictures()
    {
        ArtworkPromptRules.LetteringInstruction("A watercolor of {subject}.")
            .Should().Be("Draw every motif as a picture, never as a word or label.");
    }

    [TestMethod]
    public void CustomAvoidInstruction_LetteringAvoidsItsFaults_APictureAvoidsAnyLettering()
    {
        ArtworkPromptRules.CustomAvoidInstruction("A badge. " + PromptDefaultsResolver.CustomPhraseSentence)
            .Should().Be("Avoid: misspelled words, extra words, distorted letters, brand logos, watermark, product mockup.");
        ArtworkPromptRules.CustomAvoidInstruction("A badge.")
            .Should().Be("Avoid: text, letters, words, brand logos, watermark, signature, product mockup.");
    }

    [TestMethod]
    [DataRow("A surf van with \"Salt Life\" on top and \"EST 1985\" underneath.", "Salt Life", "A surf van with \"Salt Life\" on top.")]
    [DataRow("A retro sunset with two palm trees and the words \"Beach Vibes\".", "", "A retro sunset with two palm trees.")]
    [DataRow("A sunset with palm trees and the words \"Beach Vibes\" in bold retro letters.", "", "A sunset with palm trees.")]    [DataRow("A sunset reading “Beach Vibes”, with palm trees.", null, "A sunset, with palm trees.")]
    [DataRow("A seashell with \"salt life\" in script.", "Salt Life", "A seashell with \"salt life\" in script.")]
    [DataRow("A tiger among hibiscus flowers.", "", "A tiger among hibiscus flowers.")]
    public void WithoutOtherQuotedText_KeepsOnlyTheDesignText(string prompt, string? designText, string expected)
    {
        ArtworkPromptRules.WithoutOtherQuotedText(prompt, designText).Should().Be(expected);
    }

    [TestMethod]
    public void IsThinCustomPrompt_OnlyForAFewWords()
    {
        ArtworkPromptRules.IsThinCustomPrompt("A fox.").Should().BeTrue();
        ArtworkPromptRules.IsThinCustomPrompt(null).Should().BeTrue();
        ArtworkPromptRules.IsThinCustomPrompt("A red fox curled up asleep under a pine tree in the snow.").Should().BeFalse();
    }

    [TestMethod]
    public void AvoidInstruction_ListsTheTemplatesNegativeTerms_WithoutPlaceholders()
    {
        ArtworkPromptRules.AvoidInstruction("misspelled words, extra  words, {subject} logo, , watermark.")
            .Should().Be("Avoid: misspelled words, extra words, logo, watermark.");
        ArtworkPromptRules.AvoidInstruction(null).Should().BeEmpty();
        ArtworkPromptRules.AvoidInstruction(" {{niche}} ").Should().BeEmpty();
    }
}
