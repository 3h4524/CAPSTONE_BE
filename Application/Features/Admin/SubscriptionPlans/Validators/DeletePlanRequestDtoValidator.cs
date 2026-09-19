using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Validators;

/// <summary>
/// Validates plan deletion requests — the confirmation dialog's Reason field is required.
/// </summary>
public sealed class DeletePlanRequestDtoValidator : AbstractValidator<DeletePlanRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeletePlanRequestDtoValidator"/> class.
    /// </summary>
    public DeletePlanRequestDtoValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty();
    }
}
