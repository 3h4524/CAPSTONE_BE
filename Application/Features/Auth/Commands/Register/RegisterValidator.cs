using FluentValidation;

namespace APCS.Application.Features.Auth.Commands.Register;

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
            .MaximumLength(254);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128);

        RuleFor(command => command.FullName)
            .NotEmpty()
            .MaximumLength(150);
    }
}
