using APCS.Application.Features.BatchProductPrompts.Common;
using APCS.Application.Features.BatchProductPrompts.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchProductPrompts.Validators;

/// <summary>Validates per-row prompt overrides.</summary>
public sealed class UpdateBatchProductPromptValidator : AbstractValidator<UpdateBatchProductPromptRequestDto>
{
    public UpdateBatchProductPromptValidator()
    {
        RuleFor(request => request.Subject)
            .NotEmpty()
            .WithMessage("The Subject field is required.")
            .MaximumLength(PromptRules.MaximumSubjectLength);
        RuleFor(request => request.ArtStyle)
            .NotEmpty()
            .WithMessage("The Art Style field is required.")
            .MaximumLength(PromptRules.MaximumArtStyleLength);
        RuleFor(request => request.MoodTone)
            .NotEmpty()
            .WithMessage("The Mood Tone field is required.")
            .MaximumLength(PromptRules.MaximumMoodToneLength);
        RuleFor(request => request.NegativeTerms)
            .MaximumLength(PromptRules.MaximumNegativeTermsLength);
        RuleFor(request => request.Instructions)
            .MaximumLength(PromptRules.MaximumInstructionsLength);
    }
}
