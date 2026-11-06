namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>Features to add to a plan.</summary>
public sealed record AddPlanFeaturesRequestDto(IReadOnlyList<AddPlanFeatureDto> Features);

/// <summary>
/// One feature to add. <paramref name="LimitValue"/> sets a limit feature's value (null for no
/// limit) and is ignored on an on/off feature.
/// </summary>
public sealed record AddPlanFeatureDto(string Code, int? LimitValue = null);

/// <summary>A limit feature's new value; null means no limit.</summary>
public sealed record UpdatePlanFeatureLimitRequestDto(int? LimitValue);
