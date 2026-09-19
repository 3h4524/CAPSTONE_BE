using APCS.Application.Abstractions.Storage;
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
    }
}

/// <summary>Shared preview file checks.</summary>
public static class StyleArtPresetValidators
{
    public static bool IsSupportedImage(UploadFileDto? file) =>
        file is not null && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsWithinSizeLimit(UploadFileDto? file) =>
        file is not null && file.Length > 0 && file.Length <= StyleArtPresetRules.MaximumPreviewBytes;
}
