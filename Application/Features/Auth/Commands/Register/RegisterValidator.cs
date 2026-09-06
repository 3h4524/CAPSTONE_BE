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
            .MaximumLength(128)
            .Matches("[A-Z]")
            .WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]")
            .WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]")
            .WithMessage("Password must contain at least one digit.");

        // Optional: callers that never collected a name still register successfully, and the
        // handler falls back to the email local part.
        RuleFor(command => command.FullName)
            .MaximumLength(255);
    }
}
