using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class SaveMockupOverrideValidator : AbstractValidator<SaveMockupOverrideRequestDto>
{
    public SaveMockupOverrideValidator()
    {
        RuleFor(x => x.SlotKey)
            .NotEmpty()
            .Matches(MockupRules.SlotKeyPattern).WithMessage("This is not a mock-up of the job.");
        RuleFor(x => x.Override).NotNull();
        RuleFor(x => x.Override.MockupTemplateId).NotEmpty().When(x => x.Override is not null);
        RuleFor(x => x.Override.GarmentColor)
            .Matches(MockupRules.GarmentColorPattern).WithMessage("Garment color must look like #1F2A44.")
            .When(x => x.Override?.GarmentColor is not null);
        RuleFor(x => x.Override.BackgroundColor)
            .Matches(MockupRules.GarmentColorPattern).WithMessage("Background color must look like #EDE6DA.")
            .When(x => x.Override?.BackgroundColor is not null);
        RuleFor(x => x.Override.PrintArea)
            .Must(area => area!.X >= 0 && area.Y >= 0 && area.Width > 0 && area.Height > 0)
            .WithMessage("The print area needs a position of 0 or more and a size above 0.")
            .When(x => x.Override?.PrintArea is not null);
    }
}
