using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Validators;

/// <summary>
/// Validates subscription plan edit requests (BR195, BR197-BR199, MSG01/MSG88/MSG100).
/// </summary>
public sealed class UpdatePlanRequestDtoValidator : AbstractValidator<UpdatePlanRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatePlanRequestDtoValidator"/> class.
    /// </summary>
    public UpdatePlanRequestDtoValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty();

        RuleFor(request => request.MonthlyPriceUsd)
            .GreaterThanOrEqualTo(0m);

        RuleFor(request => request.AnnualPriceUsd)
            .GreaterThanOrEqualTo(0m)
            .When(request => request.AnnualPriceUsd is not null);

        RuleFor(request => request.MaxBatchSize).MustBeNonNegativeOrUnlimited();
        RuleFor(request => request.MaxConcurrentJobs).MustBeNonNegativeOrUnlimited();
        RuleFor(request => request.MaxProductsPerMonth).MustBeNonNegativeOrUnlimited();
        RuleFor(request => request.ImageGenerationQuota).MustBeNonNegativeOrUnlimited();
        RuleFor(request => request.VideoGenerationQuota).MustBeNonNegativeOrUnlimited();
        RuleFor(request => request.ApiCallQuota).MustBeNonNegativeOrUnlimited();

        RuleFor(request => request.StorageQuotaGb)
            .Must(value => value >= 0m || value == -1m)
            .WithMessage("Value must be greater than or equal to 0, or -1 for unlimited.");
    }
}
