using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

/// <summary>Validates mock-up template updates.</summary>
public sealed class UpdateMockupTemplateValidator : AbstractValidator<UpdateMockupTemplateRequestDto>
{
    public UpdateMockupTemplateValidator()
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
            .Must(image => image is null || MockupValidators.IsSupportedImage(image))
            .WithMessage("The blank image must be an image file.")
            .Must(image => image is null || MockupValidators.IsWithinSizeLimit(image))
            .WithMessage("The blank image cannot exceed 5 MB.");
        RuleFor(request => request.Preview)
            .Must(preview => preview is null || MockupValidators.IsSupportedImage(preview))
            .WithMessage("The preview must be an image file.")
            .Must(preview => preview is null || MockupValidators.IsWithinSizeLimit(preview))
            .WithMessage("The preview image cannot exceed 5 MB.");
        RuleFor(request => request.DeletePreview)
            .Must((request, deletePreview) => request.Preview is null || !deletePreview)
            .WithMessage("Choose either a new preview image or removing the current one, not both.");
    }
}
