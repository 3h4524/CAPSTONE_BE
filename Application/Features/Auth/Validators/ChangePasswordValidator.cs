using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates change-password requests.
/// </summary>
public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChangePasswordValidator"/> class.
    /// </summary>
    public ChangePasswordValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty();

        RuleFor(request => request.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(128)
            .Matches("[A-Z]")
            .WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]")
            .WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]")
            .WithMessage("Password must contain at least one digit.");
    }
}
