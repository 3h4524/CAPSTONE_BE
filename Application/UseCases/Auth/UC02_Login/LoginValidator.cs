using FluentValidation;

namespace APCS.Application.UseCases.Auth.UC02_Login;

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
            .MaximumLength(256);

        RuleFor(command => command.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
