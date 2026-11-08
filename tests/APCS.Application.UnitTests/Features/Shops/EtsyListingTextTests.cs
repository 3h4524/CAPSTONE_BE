using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Validators;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Shops;

[TestClass]
public sealed class EtsyListingTextTests
{
    [TestMethod]
    public void Title_RemovesCharactersEtsyRejects()
    {
        var (title, warnings) = EtsyListingText.Title("Cat Mom Shirt 🐱 | Funny Gift");
        title.Should().Be("Cat Mom Shirt | Funny Gift");
        warnings.Should().ContainSingle();
    }

    [TestMethod]
    public void Title_KeepsOnlyTheFirstOfEachOncePerTitleSymbol()
    {
        var (title, _) = EtsyListingText.Title("Mom & Dad & Kids: Matching: Shirts");
        title.Should().Be("Mom & Dad Kids: Matching Shirts");
    }

    [TestMethod]
    public void Title_LongerThan140_IsCutAtAWord()
    {
        var (title, warnings) = EtsyListingText.Title(string.Join(' ', Enumerable.Repeat("embroidered", 20)));
        title.Length.Should().BeLessThanOrEqualTo(140);
        title.Should().NotEndWith(" ").And.EndWith("embroidered");
        warnings.Should().Contain(warning => warning.Contains("140"));
    }

    [TestMethod]
    public void Tags_CleansDedupesAndCapsAtThirteen()
    {
        var raw = new[] { "cat mom!", "Cat Mom", "gift for her", "this tag is far too long for etsy" }
            .Concat(Enumerable.Range(1, 15).Select(index => $"tag {index}"));

        var (tags, warnings) = EtsyListingText.Tags(raw);

        tags.Should().HaveCount(13);
        tags[0].Should().Be("cat mom");
        tags.Should().NotContain("Cat Mom");
        warnings.Should().HaveCount(2);
    }

    private static PublishEtsyDraftRequestDto Request(string type = "physical", long? shipping = 5) =>
        new("Cat Mom Shirt", "A soft tee.", ["cat mom"], 24.99m, 999, 482, "Clothing > Shirts", type, "i_did", "made_to_order",
            shipping, null, [], [Guid.NewGuid()], false);

    [TestMethod]
    public void Validator_PhysicalListingNeedsAShippingProfile()
    {
        var validator = new PublishEtsyDraftValidator();
        validator.Validate(Request(shipping: null)).IsValid.Should().BeFalse();
        validator.Validate(Request()).IsValid.Should().BeTrue();
        validator.Validate(Request(type: "download", shipping: null) with { DesignIds = [Guid.NewGuid()] }).IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void Validator_RejectsValuesEtsyDoesNotKnow()
    {
        new PublishEtsyDraftValidator().Validate(Request() with { WhoMade = "robot" }).IsValid.Should().BeFalse();
        new PublishEtsyDraftValidator().Validate(Request() with { MockupIds = [] }).IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void Validator_DigitalListingNeedsADesignFile()
    {
        var validator = new PublishEtsyDraftValidator();
        validator.Validate(Request(type: "download", shipping: null)).IsValid.Should().BeFalse();
        validator.Validate(Request(type: "download", shipping: null) with { DesignIds = [Guid.NewGuid()] }).IsValid.Should().BeTrue();
        validator.Validate(Request(type: "download", shipping: null) with { DesignIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray() })
            .IsValid.Should().BeFalse();
    }
}
