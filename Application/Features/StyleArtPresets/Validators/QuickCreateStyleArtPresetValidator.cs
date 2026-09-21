using APCS.Application.Features.StyleArtPresets.Common;
using FluentValidation;

namespace APCS.Application.Features.StyleArtPresets.Validators;

/// <summary>Validates quick style preset creation from the prompt editor.</summary>
public sealed class QuickCreateStyleArtPresetValidator : AbstractValidator<string>
{
    public QuickCreateStyleArtPresetValidator()
    {
        RuleFor(name => name).NotEmpty().MaximumLength(StyleArtPresetRules.MaximumNameLength);
    }
}
