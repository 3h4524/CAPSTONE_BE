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

        RuleFor(request => request.TemplateColors!)
            .Must((request, byTemplate) => byTemplate.Keys.All(id => request.TemplateIds is not null && request.TemplateIds.Contains(id)))
            .WithMessage("Garment colors can only be set for the selected templates.")
            .Must(byTemplate => byTemplate.Values.All(colors => colors is null || colors.Count <= MockupRules.MaximumGarmentColors))
            .WithMessage($"Select up to {MockupRules.MaximumGarmentColors} garment colors per template.")
            .Must(byTemplate => byTemplate.Values.All(colors => colors is null || colors.Distinct(StringComparer.OrdinalIgnoreCase).Count() == colors.Count))
            .WithMessage("Garment colors of a template must not repeat.")
            .Must(byTemplate => byTemplate.Values.All(colors => colors is null
                || colors.All(color => color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, MockupRules.GarmentColorPattern))))
            .WithMessage("Garment color must look like #1F2A44.")
            .When(request => request.TemplateColors is not null);

        RuleFor(request => request.TemplateBackgrounds!)
            .Must((request, byTemplate) => byTemplate.Keys.All(id => request.TemplateIds is not null && request.TemplateIds.Contains(id)))
            .WithMessage("A background can only be set for the selected templates.")
            .Must(byTemplate => byTemplate.Values.All(byColor => byColor is { Count: > 0 and <= MockupRules.MaximumGarmentColors + 1 }
                && byColor.All(entry => entry.Value is not null && System.Text.RegularExpressions.Regex.IsMatch(entry.Value, MockupRules.GarmentColorPattern))))
            .WithMessage("Background color must look like #EDE6DA.")
            .Must(byTemplate => byTemplate.Values.All(byColor => byColor is null || byColor.Keys.All(key =>
                key == MockupRules.DefaultBackgroundKey || System.Text.RegularExpressions.Regex.IsMatch(key, MockupRules.GarmentColorPattern))))
            .WithMessage("A background goes with a garment color like #1F2A44, or with \"default\".")
            .When(request => request.TemplateBackgrounds is not null);

        RuleFor(request => request.AreaSources!)
            .Must((request, byTemplate) => byTemplate.Keys.All(id => request.TemplateIds is not null && request.TemplateIds.Contains(id)))
            .WithMessage("What is printed in a print area can only be set for the selected templates.")
            .Must(byTemplate => byTemplate.Values.All(byArea => byArea is not null && byArea.Count <= MockupRules.MaximumPrintAreas))
            .WithMessage($"A template has at most {MockupRules.MaximumPrintAreas} print areas.")
            .Must(byTemplate => byTemplate.Values.All(byArea => byArea is null || byArea.All(entry => MockupRules.AreaSourceProblem(entry.Key, entry.Value) is null)))
            .WithMessage("A print area prints the mock-up's design, another chosen design or nothing.")
            .When(request => request.AreaSources is not null);
    }
}
