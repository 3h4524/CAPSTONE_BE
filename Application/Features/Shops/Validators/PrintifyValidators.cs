using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Shops.Validators;

public sealed class CreatePrintifyProductValidator : AbstractValidator<CreatePrintifyProductRequestDto>
{
    /// <summary>Etsy takes at most 100 variations of a listing, so Printify will not publish more.</summary>
    public const int MaxVariants = 100;

    public CreatePrintifyProductValidator()
    {
        RuleFor(x => x.DesignId).NotEmpty().WithMessage("Choose the design to print.");
        RuleFor(x => x.BlueprintId).GreaterThan(0).WithMessage("Choose a product type.");
        RuleFor(x => x.PrintProviderId).GreaterThan(0).WithMessage("Choose a print provider.");
        RuleFor(x => x.VariantIds).NotEmpty().WithMessage("Choose at least one color and size.")
            .Must(ids => ids.Count <= MaxVariants).WithMessage($"Etsy allows at most {MaxVariants} variants; choose fewer colors or sizes.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("A variant is chosen twice.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(EtsyListingText.MaxTitleLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(10000);
        RuleFor(x => x.Tags).Must(tags => tags.Count <= EtsyListingText.MaxTags)
            .WithMessage($"Etsy allows at most {EtsyListingText.MaxTags} tags.");
        RuleFor(x => x.Price).GreaterThanOrEqualTo(1).LessThanOrEqualTo(10000);
        RuleFor(x => x.Scale).InclusiveBetween(0.2, 2);
        RuleFor(x => x.Position).MaximumLength(40);
        RuleFor(x => x.X).InclusiveBetween(0, 1);
        RuleFor(x => x.Y).InclusiveBetween(0, 1);
    }
}

public sealed class PublishPrintifyProductValidator : AbstractValidator<PublishPrintifyProductRequestDto>
{
    public PublishPrintifyProductValidator()
    {
        RuleFor(x => x.Prices).NotEmpty().WithMessage("Set a price for the variants you sell.")
            .Must(prices => prices.Select(price => price.VariantId).Distinct().Count() == prices.Count).WithMessage("A variant is priced twice.");
        RuleForEach(x => x.Prices).ChildRules(price =>
            price.RuleFor(x => x.Price).GreaterThanOrEqualTo(1).LessThanOrEqualTo(10000).WithMessage("Each price must be between $1 and $10,000."));
    }
}
