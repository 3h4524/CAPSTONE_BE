namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>
/// One subscription plan tier as shown on the Admin Portal — reused for both the Subscription
/// Plans list (ADM-03) and the Plan Details view (ADM-04), since the two screens show
/// overlapping data.
/// </summary>
/// <param name="WhiteLabelExportEnabled">
/// Backed by a <c>plan_features</c> row (<c>white_label_export</c>), not a dedicated column.
/// </param>
/// <param name="ActiveSubscriberCount">
/// Used by the client to preview whether a delete will hard-delete or soft-deactivate the plan
/// (BR200/BR201) before the Admin confirms.
/// </param>
public sealed record AdminPlanDto(
    Guid Id,
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
    int SortOrder,
    int ActiveSubscriberCount,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

/// <summary>
/// The outcome of a delete request (3.6.10), so the client can show the right confirmation
/// message after the fact.
/// </summary>
public sealed record DeletePlanResultDto(bool HardDeleted);
