using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.StyleArtPresets.Validators;

/// <summary>Validates seller art style creation.</summary>
public sealed class CreateStyleArtPresetValidator : AbstractValidator<CreateStyleArtPresetRequestDto>
{
    public CreateStyleArtPresetValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumNameLength);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumDescriptionLength);
        RuleFor(request => request.StyleModifiers).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumModifiersLength);
        RuleFor(request => request.RecommendationsJson)
            .Must(source => StyleArtPresetRules.TryParseRecommendations(source, out _))
            .WithMessage("Recommendations must be a JSON array of up to 8 short items.");
        RuleFor(request => request.Preview)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("A preview image is required.")
            .Must(StyleArtPresetValidators.IsSupportedImage)
            .WithMessage("The preview must be an image file.")
            .Must(StyleArtPresetValidators.IsWithinSizeLimit)
            .WithMessage("The preview image cannot exceed 5 MB.");
    }
}
