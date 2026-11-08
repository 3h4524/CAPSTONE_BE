using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Shops.Validators;

/// <summary>Etsy's own limits for a draft listing, checked before anything is sent.</summary>
public sealed class PublishEtsyDraftValidator : AbstractValidator<PublishEtsyDraftRequestDto>
{
    public static readonly string[] Types = ["physical", "download"];
    public static readonly string[] WhoMade = ["i_did", "someone_else", "collective"];
    public static readonly string[] WhenMade =
        ["made_to_order", "2020_2026", "2010_2019", "2007_2009", "before_2007", "2000_2006", "1990s", "1980s", "1970s", "1960s", "1950s", "1940s", "1930s", "1920s", "1910s", "1900s", "1800s", "1700s", "before_1700"];

    /// <summary>Etsy shows up to 20 images on a listing.</summary>
    public const int MaxImages = 20;

    public PublishEtsyDraftValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(EtsyListingText.MaxTitleLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.Tags).Must(tags => tags.Count <= EtsyListingText.MaxTags)
            .WithMessage($"Etsy allows at most {EtsyListingText.MaxTags} tags.");
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(EtsyListingText.MaxTagLength);
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(50000);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999);
        RuleFor(x => x.TaxonomyId).GreaterThan(0).WithMessage("Choose an Etsy category.");
        RuleFor(x => x.Type).Must(Types.Contains).WithMessage("Type must be physical or download.");
        RuleFor(x => x.WhoMade).Must(WhoMade.Contains);
        RuleFor(x => x.WhenMade).Must(WhenMade.Contains);
        RuleFor(x => x.ShippingProfileId).NotNull().GreaterThan(0)
            .When(x => x.Type == "physical")
            .WithMessage("Etsy needs a shipping profile for a physical listing.");
        RuleFor(x => x.MockupIds).NotEmpty().WithMessage("Choose at least one image.")
            .Must(ids => ids.Count <= MaxImages).WithMessage($"Etsy shows at most {MaxImages} images.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("An image is chosen twice.");
        // A digital listing without a file leaves the buyer with nothing to download.
        RuleFor(x => x.DesignIds).NotNull().NotEmpty()
            .When(x => x.Type == "download")
            .WithMessage("Choose at least one design file for the buyer to download.");
        RuleFor(x => x.DesignIds!)
            .Must(ids => ids.Count <= MaxFiles).WithMessage($"Etsy allows at most {MaxFiles} files on a digital listing.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("A design file is chosen twice.")
            .When(x => x.Type == "download" && x.DesignIds is not null);
    }

    /// <summary>Etsy attaches up to five files to a digital listing.</summary>
    public const int MaxFiles = 5;
}
