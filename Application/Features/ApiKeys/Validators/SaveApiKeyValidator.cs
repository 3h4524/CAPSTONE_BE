using APCS.Application.Features.ApiKeys.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.ApiKeys.Validators;

public sealed class SaveApiKeyValidator : AbstractValidator<SaveApiKeyRequestDto>
{
    public SaveApiKeyValidator()
    {
        RuleFor(x => x.Provider).Must(x => x is "openai" or "replicate" or "printify")
            .WithMessage("MSG01 — Select a supported API key provider.").WithErrorCode("MSG01");
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.Environment).Must(x => x is null or "Production" or "Sandbox")
            .WithMessage("Select Production or Sandbox.");
        RuleFor(x => x.ApiKey).Must(x => x is null || (x.Length >= 8 && x.Length <= 8192 && !x.Any(char.IsWhiteSpace) && !x.Any(char.IsControl)))
            .WithMessage("MSG01 — Enter a valid API key without whitespace (8–8192 characters).").WithErrorCode("MSG01");
        RuleFor(x => x.Confirmed).Equal(true)
            .WithMessage("MSG49 — Confirm that this key is restricted to the required permissions.").WithErrorCode("MSG49");
    }
}
