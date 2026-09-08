using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates forgot-password requests.
/// </summary>
public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForgotPasswordValidator"/> class.
    /// </summary>
    public ForgotPasswordValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
    }
}
