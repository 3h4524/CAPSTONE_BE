using APCS.Application.Features.Subscriptions.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Subscriptions.Validators;

/// <summary>
/// Validates downgrade requests.
/// </summary>
public sealed class DowngradeValidator : AbstractValidator<DowngradeRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DowngradeValidator"/> class.
    /// </summary>
    public DowngradeValidator()
    {
        // MSG54: "Please select a plan before continuing."
        RuleFor(request => request.PlanId)
            .NotEmpty();
    }
}
