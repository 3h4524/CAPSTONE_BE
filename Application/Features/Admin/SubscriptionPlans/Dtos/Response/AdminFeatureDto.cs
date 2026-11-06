namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;

/// <summary>
/// One plan feature in the Admin's catalog, with the plans that include it.
/// </summary>
/// <param name="Kind">"flag" or "limit", fixed by the backend.</param>
/// <param name="IsActive">False hides it when adding features to a plan; plans that have it keep it.</param>
/// <param name="Plans">Names of the plans that include the feature, in plan order.</param>
/// <param name="UpdatedByName">Who last edited it; null when it has its seeded values.</param>
public sealed record AdminFeatureDto(
    string Code,
    string Label,
    string Description,
    string Group,
    string Kind,
    string? Unit,
    int? DefaultLimit,
    bool TrialAvailable,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<string> Plans,
    DateTime? UpdatedAt,
    string? UpdatedByName);
