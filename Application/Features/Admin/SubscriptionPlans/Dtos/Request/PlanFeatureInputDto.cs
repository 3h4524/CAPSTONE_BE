namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>
/// One plan feature as the Admin sets it: a flag switched on or off, or a limit with its value.
/// </summary>
/// <param name="Code">One of the codes in <c>PlanFeatures.All</c>.</param>
/// <param name="LimitValue">For a limit feature, its value; null means no limit. Ignored for a flag.</param>
public sealed record PlanFeatureInputDto(
    string Code,
    bool IsEnabled,
    int? LimitValue);
