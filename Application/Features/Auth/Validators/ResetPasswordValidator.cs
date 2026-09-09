using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates reset-password requests.
/// </summary>
public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequestDto>
{
    // A generated token is a Base64Url-encoded 64-byte value; the bound is a guard against
    // oversized input reaching the hash, not a business rule.
    private const int MaximumTokenLength = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetPasswordValidator"/> class.
    /// </summary>
    public ResetPasswordValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength);

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
