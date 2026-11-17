using APCS.Application.Abstractions.AI;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Admin.SystemApiKeys.Validators;

/// <summary>Validates a platform key to add. The lengths match the <c>system_api_keys</c> columns.</summary>
public sealed class AddSystemApiKeyRequestDtoValidator : AbstractValidator<AddSystemApiKeyRequestDto>
{
    public AddSystemApiKeyRequestDtoValidator()
    {
        RuleFor(request => request.Provider)
            .Must(provider => SystemApiKeyProviders.All.Contains(provider))
            .WithMessage("Choose Ideogram or MiniMax.");
        RuleFor(request => request.Label).NotEmpty().MaximumLength(100);
        RuleFor(request => request.Key)
            .Must(key => !string.IsNullOrWhiteSpace(key) && key.Trim().Length >= 8)
            .WithMessage("Paste the whole key.")
            .MaximumLength(500);
    }
}

/// <summary>Validates a change to a platform key.</summary>
public sealed class UpdateSystemApiKeyRequestDtoValidator : AbstractValidator<UpdateSystemApiKeyRequestDto>
{
    public UpdateSystemApiKeyRequestDtoValidator()
    {
        RuleFor(request => request.Label).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Validates a provider's settings. The limits match the checks on <c>system_api_key_providers</c>.</summary>
public sealed class UpdateSystemApiKeyProviderRequestDtoValidator : AbstractValidator<UpdateSystemApiKeyProviderRequestDto>
{
    public UpdateSystemApiKeyProviderRequestDtoValidator()
    {
        RuleFor(request => request.DailyLimitPerKey)
            .InclusiveBetween(0, 1_000_000)
            .When(request => request.DailyLimitPerKey is not null);
        RuleFor(request => request.MaxConcurrentPerKey)
            .InclusiveBetween(1, 1_000)
            .When(request => request.MaxConcurrentPerKey is not null);
        RuleFor(request => request.Strategy)
            .Must(strategy => strategy is SystemApiKeyStrategies.Fill or SystemApiKeyStrategies.Rotate)
            .WithMessage("Choose fill or rotate.");
    }
}
