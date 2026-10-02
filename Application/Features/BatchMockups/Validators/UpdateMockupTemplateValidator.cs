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
        RuleFor(x => x.X).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Width).GreaterThan(0);
        RuleFor(x => x.Height).GreaterThan(0);
        // Unlike Create, the photo is optional here — omitting it keeps the existing one.
        RuleFor(x => x.BaseImage)
            .Must(MockupImageValidators.IsSupportedImage).WithMessage("The base photo must be an image file.")
            .Must(MockupImageValidators.IsWithinSizeLimit).WithMessage("The base photo must be 10 MB or smaller.")
            .When(x => x.BaseImage is not null);
    }
}
