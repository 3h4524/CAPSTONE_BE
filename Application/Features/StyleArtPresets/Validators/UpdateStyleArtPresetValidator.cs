using APCS.Application.Features.StyleArtPresets.Common;
using APCS.Application.Features.StyleArtPresets.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.StyleArtPresets.Validators;

/// <summary>Validates seller art style updates.</summary>
public sealed class UpdateStyleArtPresetValidator : AbstractValidator<UpdateStyleArtPresetRequestDto>
{
    public UpdateStyleArtPresetValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumNameLength);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumDescriptionLength);
        RuleFor(request => request.StyleModifiers).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumModifiersLength);
        RuleFor(request => request.RecommendationsJson)
            .Must(source => StyleArtPresetRules.TryParseRecommendations(source, out _))
            .WithMessage("Recommendations must be a JSON array of up to 8 short items.");
        RuleFor(request => request.Preview)
            .Must(preview => preview is null || StyleArtPresetValidators.IsSupportedImage(preview))
            .WithMessage("The preview must be an image file.")
            .Must(preview => preview is null || StyleArtPresetValidators.IsWithinSizeLimit(preview))
            .WithMessage("The preview image cannot exceed 5 MB.");
        RuleFor(request => request.DeletePreview)
            .Must((request, deletePreview) => request.Preview is null || !deletePreview)
            .WithMessage("Choose either a new preview image or removing the current one, not both.");
    }
}
