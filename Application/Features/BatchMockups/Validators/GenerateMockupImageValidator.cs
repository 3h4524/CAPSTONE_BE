using APCS.Application.Features.BatchMockups.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.BatchMockups.Validators;

public sealed class GenerateMockupImageValidator : AbstractValidator<GenerateMockupImageRequestDto>
{
    public GenerateMockupImageValidator()
    {
        // Position fields are all-or-nothing: either every one is given (advanced override) or none
        // are (quick mode, falls back to the template's own print area).
        RuleFor(x => x)
            .Must(x => (x.X is null && x.Y is null && x.Width is null && x.Height is null)
                    || (x.X is not null && x.Y is not null && x.Width is not null && x.Height is not null))
            .WithMessage("Provide X, Y, Width and Height together, or none of them.");
        RuleFor(x => x.X!.Value).GreaterThanOrEqualTo(0).When(x => x.X is not null);
        RuleFor(x => x.Y!.Value).GreaterThanOrEqualTo(0).When(x => x.Y is not null);
        RuleFor(x => x.Width!.Value).GreaterThan(0).When(x => x.Width is not null);
        RuleFor(x => x.Height!.Value).GreaterThan(0).When(x => x.Height is not null);
    }
}
