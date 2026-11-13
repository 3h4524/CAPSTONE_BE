using APCS.Application.Features.Social;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Social;

[TestClass]
public sealed class SocialCaptionBuilderTests
{
    private const string LongTitle =
        "Funny Math Teacher Shirt, Pi Day Shirt, Algebra Teacher Gift, Math Pun Tee, Nerdy Teacher Shirt, Back To School Gift For Teachers";

    private static SocialListingText Listing(string? url = "https://www.etsy.com/listing/1") => new(LongTitle,
        "A soft tee for teachers who love a good pun.\n\n• 100% cotton\n• Printed in the USA\n\nWash cold, inside out.",
        ["math pun shirt", "pi day shirt", "algebra teacher", "teacher gift", "nerdy teacher", "back to school"], url);

    [TestMethod]
    public void Pinterest_KeepsWholePhrasesWithinTheTitleLimit_AndListsTheListingsKeywords()
    {
        var pin = SocialCaptionBuilder.Pinterest(Listing(), "Math tee");

        pin.Title.Should().Be("Funny Math Teacher Shirt, Pi Day Shirt, Algebra Teacher Gift, Math Pun Tee, Nerdy Teacher Shirt");
        pin.Title.Length.Should().BeLessThanOrEqualTo(SocialCaptionBuilder.PinterestTitleMax);
        pin.Description.Should().StartWith("A soft tee for teachers who love a good pun.").And.Contain("Shop it on Etsy.")
            .And.EndWith("math pun shirt · pi day shirt · algebra teacher · teacher gift · nerdy teacher");
        pin.Description.Length.Should().BeLessThanOrEqualTo(SocialCaptionBuilder.PinterestDescriptionMax);
        pin.AltText.Should().Contain("mock-up").And.Contain("Math tee");
    }

    [TestMethod]
    public void Pinterest_WithoutAListing_DoesNotSendBuyersToEtsy()
    {
        SocialCaptionBuilder.Pinterest(Listing(url: null), "Math tee").Description.Should().NotContain("Etsy");
    }

    [TestMethod]
    public void Pinterest_ALongDescription_StaysWithinTheLimit_AtASentence()
    {
        var listing = Listing() with { Description = string.Join(' ', Enumerable.Repeat("This shirt is soft and warm for every school day.", 30)) };

        var description = SocialCaptionBuilder.Pinterest(listing, "Math tee").Description;

        description.Length.Should().BeLessThanOrEqualTo(SocialCaptionBuilder.PinterestDescriptionMax);
        description.Split("\n\n")[0].Should().EndWith("day. Shop it on Etsy.");
    }

    [TestMethod]
    public void TikTok_LeadsWithTheProduct_PointsToTheShop_AndEndsWithHashtagsFromTheTags()
    {
        var caption = SocialCaptionBuilder.TikTok(Listing());

        caption.Should().StartWith("Funny Math Teacher Shirt ✨").And.Contain("link in bio")
            .And.EndWith("#mathpunshirt #pidayshirt #algebrateacher #teachergift #nerdyteacher");
    }

    [TestMethod]
    public void Hashtags_SkipUnusableTags_AndFillUpWithShopHashtags()
    {
        SocialCaptionBuilder.Hashtags(["Math Pun Shirt", "math-pun shirt", "a", "custom name tee"], 5)
            .Should().Equal("#mathpunshirt", "#customnametee", "#etsyfinds", "#smallbusiness", "#shopsmall");
    }
}
