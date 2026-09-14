namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The Seller's currently active subscription, for the Current Plan summary card.
/// </summary>
public sealed record CurrentSubscriptionDto(
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    string? ShortDescription,
    string Status,
    string BillingCycle,
    decimal Price,
    DateOnly StartDate,
    DateOnly RenewalDate);
