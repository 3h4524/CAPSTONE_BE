using APCS.Application.Features.Auth.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Auth.Validators;

/// <summary>
/// Validates Google sign-in requests.
/// </summary>
public sealed class GoogleLoginValidator : AbstractValidator<GoogleLoginRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleLoginValidator"/> class.
    /// </summary>
    public GoogleLoginValidator()
    {
        RuleFor(request => request.IdToken)
            .NotEmpty();
    }
}
