using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchProductPrompts.Validators;

/// <summary>Validates per-row prompt overrides.</summary>
public sealed class UpdateBatchProductPromptValidator : AbstractValidator<UpdateBatchProductPromptRequestDto>
{
    public UpdateBatchProductPromptValidator()
    {
        // A template needs its subject, style and mood; a custom image prompt says all of it itself, and its subject is
        // the design text, which may be empty.
        When(request => string.IsNullOrWhiteSpace(request.CustomPrompt), () =>
        {
            RuleFor(request => request.Subject).NotEmpty().WithMessage("The Subject field is required.");
            RuleFor(request => request.ArtStyle).NotEmpty().WithMessage("The Art Style field is required.");
            RuleFor(request => request.MoodTone).NotEmpty().WithMessage("The Mood Tone field is required.");
        });
        RuleFor(request => request.Subject).MaximumLength(PromptRules.MaximumSubjectLength);
        RuleFor(request => request.ArtStyle).MaximumLength(PromptRules.MaximumArtStyleLength);
        RuleFor(request => request.MoodTone).MaximumLength(PromptRules.MaximumMoodToneLength);
        RuleFor(request => request.CustomPrompt).MaximumLength(PromptRules.MaximumCustomPromptLength);
        RuleFor(request => request.NegativeTerms)
            .MaximumLength(PromptRules.MaximumNegativeTermsLength);
        RuleFor(request => request.Instructions)
            .MaximumLength(PromptRules.MaximumInstructionsLength);
    }
}
