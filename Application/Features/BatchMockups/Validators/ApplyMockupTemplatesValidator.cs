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

        RuleFor(request => request.GarmentColors!)
            .Must(colors => colors.Count <= MockupRules.MaximumGarmentColors)
            .WithMessage($"Select up to {MockupRules.MaximumGarmentColors} garment colors.")
            .Must(colors => colors.Distinct(StringComparer.OrdinalIgnoreCase).Count() == colors.Count)
            .WithMessage("Garment colors must not repeat.")
            .When(request => request.GarmentColors is not null);
        RuleForEach(request => request.GarmentColors)
            .Matches(MockupRules.GarmentColorPattern).WithMessage("Garment color must look like #1F2A44.")
            .When(request => request.GarmentColors is not null);
    }
}
