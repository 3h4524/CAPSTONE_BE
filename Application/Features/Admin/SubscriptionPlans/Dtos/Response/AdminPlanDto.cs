namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>
/// One subscription plan tier as shown on the Admin Portal — reused for both the Subscription
/// Plans list (ADM-03) and the Plan Details view (ADM-04), since the two screens show
/// overlapping data.
/// </summary>
/// <param name="WhiteLabelExportEnabled">
/// Backed by a <c>plan_features</c> row (<c>white_label_export</c>), not a dedicated column.
/// </param>
/// <param name="ActiveSubscriberCount">Shown to the Admin as context, not a delete gate.</param>
/// <param name="CanDelete">
/// True only when no subscription (active or historical) ever referenced this plan — the exact
/// condition the Delete action itself checks (BR200). The client uses this to disable the trash
/// action up front instead of letting the Admin attempt it and hit a Conflict.
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
    bool CanDelete,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
