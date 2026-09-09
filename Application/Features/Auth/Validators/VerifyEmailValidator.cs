using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates email verification requests.
/// </summary>
public sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailRequestDto>
{
    // A generated token is a Base64Url-encoded 64-byte value; the bound is a guard against
    // oversized input reaching the hash, not a business rule.
    private const int MaximumTokenLength = 256;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyEmailValidator"/> class.
    /// </summary>
    public VerifyEmailValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty()
            .MaximumLength(MaximumTokenLength);
    }
}
