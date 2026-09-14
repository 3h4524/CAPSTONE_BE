namespace APCS.Application.Features.Subscriptions.Dtos.Request;

/// <summary>
/// Initiates a real PayOS checkout for a subscription plan.
/// </summary>
/// <param name="PlanId">The plan being purchased.</param>
/// <param name="BillingCycle">Either "monthly" or "annual".</param>
/// <remarks>Use case mapping: UC59 Buy Subscription Plan, UC60 Process Payment.</remarks>
public sealed record CheckoutRequestDto(Guid PlanId, string BillingCycle);
