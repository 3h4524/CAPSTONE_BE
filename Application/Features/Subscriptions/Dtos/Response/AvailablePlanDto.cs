namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// One plan card in the Available Plans comparison (BR91-93).
/// </summary>
/// <param name="AnnualPriceUsd">
/// Null when the plan has no annual price; the client must not offer an Annual billing option
/// for a plan with a null value here (BR191).
/// </param>
/// <param name="IsCurrentPlan">True for the card matching the Seller's active subscription (BR92).</param>
public sealed record AvailablePlanDto(
    Guid PlanId,
    string Name,
    string Tier,
    string? Description,
    decimal MonthlyPriceUsd,
    decimal? AnnualPriceUsd,
    bool IsCurrentPlan,
    IReadOnlyList<PlanFeatureFlagDto> Features,
    PlanQuotasDto Quotas);

/// <summary>
/// One feature flag/limit row for a plan, passed through generically since the actual
/// <c>FeatureCode</c> values are seeded data owned by the database, not the application.
/// </summary>
public sealed record PlanFeatureFlagDto(
    string FeatureCode,
    bool IsEnabled,
    int? LimitValue);

/// <summary>
/// The plan-limit fields shown on a comparison card beyond the D2 usage-quota cards
/// (video generation, batch size, products/month, concurrent jobs have no usage-tracking
/// counterpart yet, so they are only ever shown as plain limits, never as a progress bar).
/// </summary>
public sealed record PlanQuotasDto(
    int ImageGenerationQuota,
    int VideoGenerationQuota,
    int ApiCallQuota,
    decimal StorageQuotaGb,
    int MaxBatchSize,
    int MaxProductsPerMonth,
    int MaxConcurrentJobs);
