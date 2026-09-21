using APCS.Application.Features.DesignTemplates.Common;
using FluentValidation;

namespace APCS.Application.Features.DesignTemplates.Validators;

/// <summary>Validates a few-shot design-template example.</summary>
public sealed class DesignTemplateExampleValidator : AbstractValidator<DesignTemplateExampleDto>
{
    public DesignTemplateExampleValidator()
    {
        RuleFor(example => example.Subject).NotEmpty();
        RuleFor(example => example.Prompt).NotEmpty();
    }
}
