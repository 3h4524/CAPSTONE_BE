using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates resend verification requests.
/// </summary>
public sealed class ResendVerificationEmailValidator : AbstractValidator<ResendVerificationEmailRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResendVerificationEmailValidator"/> class.
    /// </summary>
    public ResendVerificationEmailValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
    }
}
