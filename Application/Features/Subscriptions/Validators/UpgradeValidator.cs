using APCS.Application.Features.Subscriptions.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Subscriptions.Validators;

/// <summary>
/// Validates upgrade requests.
/// </summary>
public sealed class UpgradeValidator : AbstractValidator<UpgradeRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpgradeValidator"/> class.
    /// </summary>
    public UpgradeValidator()
    {
        // MSG54: "Please select a plan before continuing."
        RuleFor(request => request.PlanId)
            .NotEmpty();
    }
}
