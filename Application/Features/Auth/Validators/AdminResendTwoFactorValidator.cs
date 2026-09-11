using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates an administrator OTP resend request.
/// </summary>
public sealed class AdminResendTwoFactorValidator : AbstractValidator<AdminResendTwoFactorRequestDto>
{
    public AdminResendTwoFactorValidator()
    {
        RuleFor(request => request.TempToken).NotEmpty().MaximumLength(200);
    }
}