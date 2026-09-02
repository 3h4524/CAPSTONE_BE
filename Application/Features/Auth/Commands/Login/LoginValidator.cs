using FluentValidation;

namespace APCS.Application.Features.Auth.Commands.Login;

/// <summary>
/// Validates login requests.
/// </summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoginValidator"/> class.
    /// </summary>
    public LoginValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
