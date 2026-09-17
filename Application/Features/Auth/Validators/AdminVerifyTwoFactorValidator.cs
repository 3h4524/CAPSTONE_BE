using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates an administrator OTP verification request.
/// </summary>
public sealed class AdminVerifyTwoFactorValidator : AbstractValidator<AdminVerifyTwoFactorRequestDto>
{
    public AdminVerifyTwoFactorValidator()
    {
        RuleFor(request => request.TempToken).NotEmpty().MaximumLength(200);
        RuleFor(request => request.OtpCode).Matches("^[0-9]{6}$");
    }
}