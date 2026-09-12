using APCS.Application.Features.Profile.Common;
using APCS.Application.Features.Profile.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Profile.Validators;

/// <summary>
/// Validates partial profile updates.
/// </summary>
public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateProfileValidator"/> class.
    /// </summary>
    public UpdateProfileValidator()
    {
        RuleFor(request => request.FullName)
            .NotEmpty()
            .MaximumLength(100)
            .When(request => request.FullName is not null);

        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254)
            .When(request => request.Email is not null);

        RuleFor(request => request.ShopName)
            .MaximumLength(100)
            .When(request => request.ShopName is not null);

        RuleFor(request => request.ShopDescription)
            .MaximumLength(100)
            .When(request => request.ShopDescription is not null);

        RuleFor(request => request.Timezone)
            .NotEmpty()
            .Must(timezone => ProfileOptions.Timezones.Contains(timezone))
            .WithMessage("Timezone is not supported.")
            .When(request => request.Timezone is not null);

        RuleFor(request => request.Language)
            .NotEmpty()
            .Must(language => ProfileOptions.Languages.Contains(language))
            .WithMessage("Language is not supported.")
            .When(request => request.Language is not null);

        RuleFor(request => request.ThemePreference)
            .Must(theme => ProfileOptions.ThemePreferences.Contains(theme))
            .WithMessage("Theme preference is not supported.")
            .When(request => !string.IsNullOrWhiteSpace(request.ThemePreference));
    }
}
