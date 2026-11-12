using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.Batches.Validators;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class UpdateMockupTemplateValidator : AbstractValidator<UpdateMockupTemplateRequestDto>
{
    public UpdateMockupTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MockupRules.MaximumNameLength);
        RuleFor(x => x.ProductType).Must(ProductRules.IsValidType).WithMessage("Choose a supported product type.");
        // Several print areas take the place of the one rectangle.
        When(x => x.Areas is null, () =>
        {
            RuleFor(x => x.X).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Width).GreaterThan(0);
            RuleFor(x => x.Height).GreaterThan(0);
        });
        RuleFor(x => x.Areas)
            .Must(areas => MockupRules.AreasProblem(areas, forTemplate: true) is null)
            .WithMessage(x => MockupRules.AreasProblem(x.Areas, forTemplate: true)!)
            .When(x => x.Areas is not null);
        RuleFor(x => x.View)
            .Must(view => MockupRules.Views.Contains(view!)).WithMessage("The photo shows the front, back, a sleeve or the side of the garment.")
            .When(x => x.View is not null);
        RuleFor(x => x.GarmentColor)
            .Matches(MockupRules.GarmentColorPattern).WithMessage("Garment color must look like #1F2A44.")
            .When(x => x.GarmentColor is not null);
        // Unlike Create, the photo is optional here — omitting it keeps the existing one.
        RuleFor(x => x.BaseImage)
            .Must(MockupImageValidators.IsSupportedImage).WithMessage("The base photo must be an image file.")
            .Must(MockupImageValidators.IsWithinSizeLimit).WithMessage("The base photo must be 10 MB or smaller.")
            .When(x => x.BaseImage is not null);
    }
}
