namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos;

/// <summary>The new plan's name and tier slug; everything else is copied from the plan duplicated.</summary>
public sealed record DuplicatePlanRequestDto(string Name, string Tier);

/// <summary>The new plan, not on sale until the Admin puts it on sale.</summary>
public sealed record DuplicatedPlanDto(Guid PlanId, string Name, string Tier);

/// <summary>Every plan's id, in the order Sellers and the Admin should see them.</summary>
public sealed record ReorderPlansRequestDto(IReadOnlyList<Guid> PlanIds);

/// <summary>
/// One change to the plans, Free or the trial, as the History tab lists it.
/// </summary>
/// <param name="Kind">"plan", "feature", "limit", "catalog", "settings", "duplicate" or "order".</param>
/// <param name="PlanName">The plan changed, when the change was to one plan.</param>
/// <param name="Summary">What changed, in a sentence.</param>
public sealed record PlanHistoryEntryDto(
    Guid Id,
    DateTime At,
    string? ActorName,
    string Kind,
    string? PlanName,
    string Summary);
