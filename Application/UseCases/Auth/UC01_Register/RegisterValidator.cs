using FluentValidation;

namespace APCS.Application.UseCases.Auth.UC01_Register;

/// <summary>
/// Validates register requests.
/// </summary>
public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterValidator"/> class.
    /// </summary>
    public RegisterValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);

        RuleFor(command => command.FullName)
            .MaximumLength(150);
    }
}
