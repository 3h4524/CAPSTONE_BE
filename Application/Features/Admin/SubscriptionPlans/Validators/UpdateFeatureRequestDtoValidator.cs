using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Validators;

/// <summary>
/// Validates an edit to a plan feature in the catalog. The lengths match the <c>features</c> columns.
/// </summary>
public sealed class UpdateFeatureRequestDtoValidator : AbstractValidator<UpdateFeatureRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateFeatureRequestDtoValidator"/> class.
    /// </summary>
    public UpdateFeatureRequestDtoValidator()
    {
        RuleFor(request => request.Label).NotEmpty().MaximumLength(120);
        RuleFor(request => request.Description).MaximumLength(500);
        RuleFor(request => request.Group).NotEmpty().MaximumLength(60);
        RuleFor(request => request.Unit).MaximumLength(40);
        RuleFor(request => request.DefaultLimit)
            .InclusiveBetween(0, 10_000)
            .When(request => request.DefaultLimit is not null);
        RuleFor(request => request.SortOrder).InclusiveBetween(0, 10_000);
    }
}
