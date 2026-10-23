using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class SaveMockupOverridesValidator : AbstractValidator<SaveMockupOverridesRequestDto>
{
    public SaveMockupOverridesValidator()
    {
        RuleFor(x => x.Overrides)
            .NotEmpty().WithMessage("Choose at least one mock-up to change.")
            .Must(items => items.Count <= MockupRules.MaxOverridesPerSave)
            .WithMessage($"Change at most {MockupRules.MaxOverridesPerSave} mock-ups at once.")
            .Must(items => items.Where(item => item is not null).Select(item => item.SlotKey).Distinct().Count() == items.Count(item => item is not null))
            .WithMessage("Each mock-up can be changed only once in a save.");
        RuleForEach(x => x.Overrides).NotNull().SetValidator(new SaveMockupOverrideValidator());
    }
}
