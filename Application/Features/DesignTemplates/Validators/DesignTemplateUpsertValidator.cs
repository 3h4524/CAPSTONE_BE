using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.DesignTemplates.Validators;

/// <summary>Provides validation rules shared by template creation and editing.</summary>
public abstract class DesignTemplateUpsertValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IDesignTemplateUpsertRequest
{
    protected DesignTemplateUpsertValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(DesignTemplateRules.MaximumNameLength);
        RuleFor(request => request.NicheCategory)
            .NotEmpty()
            .Must(DesignTemplateNiches.All.Contains)
            .WithMessage("Select a supported niche category.");
        RuleFor(request => request.ArtStyle)
            .NotEmpty()
            .Must(DesignTemplateArtStyles.All.Contains)
            .WithMessage("Select a supported art style.");
        RuleFor(request => request.BasePrompt)
            .NotEmpty()
            .MaximumLength(DesignTemplateRules.MaximumBasePromptLength)
            .Must(DesignTemplatePromptRules.ContainsOnlySupportedPlaceholders)
            .WithMessage("The base prompt contains an unsupported placeholder.");
        RuleFor(request => request.NegativePrompt)
            .MaximumLength(DesignTemplateRules.MaximumBasePromptLength)
            .Must(prompt => string.IsNullOrWhiteSpace(prompt) || DesignTemplatePromptRules.ContainsOnlySupportedPlaceholders(prompt))
            .WithMessage("The negative prompt contains an unsupported placeholder.");
        RuleFor(request => request.Examples)
            .NotNull()
            .Must(examples => examples.Count <= DesignTemplateRules.MaximumExamples)
            .WithMessage($"A template can contain at most {DesignTemplateRules.MaximumExamples} examples.");
        RuleForEach(request => request.Examples).SetValidator(new DesignTemplateExampleValidator());
    }
}
