namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// What the Seller's current plan allows, so the client can lock what the plan does not include.
/// </summary>
/// <param name="HasPlan">False on Free, which runs within its monthly allowances (see <c>GET /api/allowances</c>).</param>
/// <param name="MaxBatchSize">Products per run; -1 means no limit.</param>
/// <param name="MaxConcurrentJobs">Design jobs running at once; -1 means no limit.</param>
public sealed record EntitlementsResponseDto(
    bool HasPlan,
    string? PlanName,
    string? Tier,
    bool IsTrial,
    DateTime? TrialEndsAt,
    int MaxBatchSize,
    int MaxConcurrentJobs,
    bool PrioritySupport,
    IReadOnlyList<EntitlementFeatureDto> Features);

/// <summary>
/// One plan feature as it applies to the Seller.
/// </summary>
/// <param name="Kind">"flag" or "limit".</param>
/// <param name="LimitValue">For a limit, its value; null means no limit.</param>
public sealed record EntitlementFeatureDto(
    string Code,
    string Label,
    string Kind,
    string Group,
    bool IsEnabled,
    int? LimitValue);
