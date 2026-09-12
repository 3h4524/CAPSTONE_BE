using APCS.Application.Features.Subscriptions.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Subscriptions.Validators;

/// <summary>
/// Validates checkout requests.
/// </summary>
public sealed class CheckoutValidator : AbstractValidator<CheckoutRequestDto>
{
    private static readonly string[] AllowedBillingCycles = ["monthly", "annual"];

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckoutValidator"/> class.
    /// </summary>
    public CheckoutValidator()
    {
        RuleFor(request => request.PlanId)
            .NotEmpty();

        RuleFor(request => request.BillingCycle)
            .NotEmpty()
            .Must(cycle => AllowedBillingCycles.Contains(cycle.Trim().ToLowerInvariant()))
            .WithMessage("Billing cycle must be either 'monthly' or 'annual'.");
    }
}
