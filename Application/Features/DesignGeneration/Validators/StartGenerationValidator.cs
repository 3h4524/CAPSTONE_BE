using APCS.Application.Features.DesignGeneration.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.DesignGeneration.Validators;

public sealed class StartGenerationValidator : AbstractValidator<StartGenerationRequestDto>
{
    private static readonly string[] AllowedAspectRatios = ["1:1", "16:9", "9:16", "4:3", "3:4"];

    public StartGenerationValidator()
    {
        RuleFor(x => x.VariationCount).InclusiveBetween(1, 4)
            .WithMessage("Choose between 1 and 4 image variations per product.");
        RuleFor(x => x.AspectRatio).Must(x => AllowedAspectRatios.Contains(x))
            .WithMessage($"Aspect ratio must be one of: {string.Join(", ", AllowedAspectRatios)}.");
    }
}
