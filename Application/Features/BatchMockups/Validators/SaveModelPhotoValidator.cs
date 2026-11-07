using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.Batches.Validators;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class SaveModelPhotoValidator : AbstractValidator<SaveModelPhotoRequestDto>
{
    public SaveModelPhotoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MockupRules.MaximumNameLength);
        RuleFor(x => x.ProductType).Must(ProductRules.IsValidType).WithMessage("Choose a supported product type.");
        RuleFor(x => x.X).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Y).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Width).GreaterThan(0);
        RuleFor(x => x.Height).GreaterThan(0);
        RuleFor(x => x.Photo)
            .Must(MockupImageValidators.IsSupportedImage).WithMessage("The photo must be an image file.")
            .Must(MockupImageValidators.IsWithinSizeLimit).WithMessage("The photo must be 10 MB or smaller.")
            .When(x => x.Photo is not null);
        RuleFor(x => x.PrintQuad)
            .Must(quad => quad!.Count == 4 && MockupRules.IsValidQuad(quad, int.MaxValue, int.MaxValue))
            .WithMessage("Place the print area's four corners clockwise from the top-left, without crossing.")
            .When(x => x.PrintQuad is not null && !x.AutoPlacement);
        RuleFor(x => x.Attributes)
            .Must(attributes => ModelPeople.Problem(attributes!) is null)
            .WithMessage(x => ModelPeople.Problem(x.Attributes!) ?? "")
            .When(x => x.Attributes is not null);
        RuleFor(x => x.GarmentSelection)
            .Must(MockupImageValidators.IsSupportedImage).WithMessage("The garment selection must be an image.")
            .Must(MockupImageValidators.IsWithinSizeLimit).WithMessage("The garment selection must be 10 MB or smaller.")
            .When(x => x.GarmentSelection is not null);
    }
}
