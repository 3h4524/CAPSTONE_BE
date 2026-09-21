namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>
/// Creates a new subscription plan tier (ADM-05 Add Subscription Plan).
/// </summary>
/// <param name="Tier">
/// Lowercase, space-free API-referencing slug. Unique (BR194) and immutable after creation.
/// </param>
/// <param name="MonthlyPriceUsd">Must be &gt;= 0 (BR195).</param>
/// <param name="AnnualPriceUsd">Null when the plan offers no annual billing option.</param>
/// <param name="MaxBatchSize">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="MaxConcurrentJobs">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="MaxProductsPerMonth">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="ImageGenerationQuota">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="VideoGenerationQuota">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="ApiCallQuota">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="StorageQuotaGb">Must be &gt;= 0, or -1 for unlimited (BR195).</param>
/// <param name="IsActive">True makes the plan immediately available to Sellers (BR196).</param>
public sealed record CreatePlanRequestDto(
    string Name,
    string Tier,
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
