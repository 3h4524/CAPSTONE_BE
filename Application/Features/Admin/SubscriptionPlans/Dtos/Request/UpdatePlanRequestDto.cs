namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>
/// Updates an existing subscription plan tier (3.6.9 Edit Subscription Plan). The Tier slug is
/// intentionally absent — BR197 makes it immutable after creation.
/// </summary>
public sealed record UpdatePlanRequestDto(
    string Name,
    string? Description,
    decimal MonthlyPriceUsd,
    decimal? AnnualPriceUsd,
    int MaxBatchSize,
    int MaxConcurrentJobs,
    int MaxProductsPerMonth,
    int ImageGenerationQuota,
    int VideoGenerationQuota,
    int ApiCallQuota,
    decimal StorageQuotaGb,
    bool CustomApiKeysAllowed,
    bool WhiteLabelExportEnabled,
    bool PrioritySupport,
    bool IsActive,
    int SortOrder);
