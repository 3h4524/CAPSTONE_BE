namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>
/// How a plan feature is shown. The code and kind are fixed by the backend and cannot change.
/// </summary>
/// <param name="Unit">For a limit, what it counts, such as "per day". Ignored on a flag.</param>
/// <param name="DefaultLimit">For a limit, the value offered when adding it to a plan; null for no limit. Ignored on a flag.</param>
/// <param name="TrialAvailable">Whether the free trial includes the feature when its plan does.</param>
/// <param name="IsActive">Whether the feature is offered when adding features to a plan. Plans that have it keep it.</param>
public sealed record UpdateFeatureRequestDto(
    string Label,
    string? Description,
    string Group,
    string? Unit,
    int? DefaultLimit,
    bool TrialAvailable,
    bool IsActive,
    int SortOrder);
