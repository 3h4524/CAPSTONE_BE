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
        RuleFor(x => x.X).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Width).GreaterThan(0);
        RuleFor(x => x.Height).GreaterThan(0);
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
