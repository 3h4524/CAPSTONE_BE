using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

/// <summary>Validates mock-up template creation.</summary>
public sealed class CreateMockupTemplateValidator : AbstractValidator<CreateMockupTemplateRequestDto>
{
    public CreateMockupTemplateValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(120);
        RuleFor(request => request.ProductType)
            .Must(type => MockupRules.ValidProductTypes.Contains(type))
            .WithMessage("The product type is invalid.");
        RuleFor(request => request.PrintAreaConfig)
            .Must(MockupValidators.IsJsonObject)
            .WithMessage("The print area must be a valid JSON object.");
        RuleFor(request => request.OutputWidthPx).InclusiveBetween(100, 10000);
        RuleFor(request => request.OutputHeightPx).InclusiveBetween(100, 10000);
        RuleFor(request => request.BaseImage)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("A blank mock-up image is required.")
            .Must(MockupValidators.IsSupportedImage)
            .WithMessage("The blank image must be an image file.")
            .Must(MockupValidators.IsWithinSizeLimit)
            .WithMessage("The blank image cannot exceed 5 MB.");
        RuleFor(request => request.Preview)
            .Must(preview => preview is null || MockupValidators.IsSupportedImage(preview))
            .WithMessage("The preview must be an image file.")
            .Must(preview => preview is null || MockupValidators.IsWithinSizeLimit(preview))
            .WithMessage("The preview image cannot exceed 5 MB.");
    }
}
