using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using FluentValidation;

namespace APCS.Application.Features.Admin.SubscriptionPlans.Validators;

/// <summary>
/// Validates new subscription plan requests (BR193-BR196, MSG01/MSG88/MSG100).
/// </summary>
public sealed class CreatePlanRequestDtoValidator : AbstractValidator<CreatePlanRequestDto>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePlanRequestDtoValidator"/> class.
    /// </summary>
    public CreatePlanRequestDtoValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty();

        RuleFor(request => request.Tier)
            .NotEmpty()
            .Matches("^[a-z0-9-]+$")
            .WithMessage("Tier slug must be lowercase and contain no spaces.");

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

/// <summary>
/// Shared BR195 rule: a usage limit must be non-negative, or exactly -1 to mean "unlimited".
/// </summary>
internal static class PlanQuotaValidationExtensions
{
    public static IRuleBuilderOptions<T, int> MustBeNonNegativeOrUnlimited<T>(this IRuleBuilder<T, int> ruleBuilder) =>
        ruleBuilder
            .Must(value => value >= 0 || value == -1)
            .WithMessage("Value must be greater than or equal to 0, or -1 for unlimited.");
}
