using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

/// <summary>Validates batch mock-up selection shape.</summary>
public sealed class ApplyMockupTemplatesValidator : AbstractValidator<ApplyMockupTemplatesRequestDto>
{
    public ApplyMockupTemplatesValidator()
    {
        RuleFor(request => request.TemplateIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Select at least one mock-up template.")
            .Must(ids => ids.Count is >= MockupRules.MinimumSelection and <= MockupRules.MaximumSelection)
            .WithMessage($"Select between {MockupRules.MinimumSelection} and {MockupRules.MaximumSelection} mock-up templates.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Mock-up templates must not repeat.");
    }
}
