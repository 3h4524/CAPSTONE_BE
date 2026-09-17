namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The Seller's currently active subscription, for the Current Plan summary card.
/// </summary>
/// <param name="ScheduledPlanName">
/// Set when a downgrade is scheduled — "Downgrading to [ScheduledPlanName] on
/// [ScheduledPlanEffectiveDate]". Null when nothing is scheduled.
/// </param>
public sealed record CurrentSubscriptionDto(
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    string? ShortDescription,
    string Status,
    string BillingCycle,
    decimal Price,
    DateOnly StartDate,
    DateOnly RenewalDate,
    string? ScheduledPlanName,
    DateOnly? ScheduledPlanEffectiveDate);
