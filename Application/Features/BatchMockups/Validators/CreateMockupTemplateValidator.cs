using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.Batches.Validators;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class CreateMockupTemplateValidator : AbstractValidator<CreateMockupTemplateRequestDto>
{
    public CreateMockupTemplateValidator()
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
        // Another side of a template is its back or a sleeve.
        RuleFor(x => x.View)
            .Must(view => view is not null && MockupRules.OtherViews.Contains(view)).WithMessage("Another side is the back or a sleeve.")
            .When(x => x.MainTemplateId is not null);
        RuleFor(x => x.GarmentColor)
            .Matches(MockupRules.GarmentColorPattern).WithMessage("Garment color must look like #1F2A44.")
            .When(x => x.GarmentColor is not null);
        RuleFor(x => x.BaseImage)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A base mock-up photo is required.")
            .Must(MockupImageValidators.IsSupportedImage).WithMessage("The base photo must be an image file.")
            .Must(MockupImageValidators.IsWithinSizeLimit).WithMessage("The base photo must be 10 MB or smaller.");
    }
}
