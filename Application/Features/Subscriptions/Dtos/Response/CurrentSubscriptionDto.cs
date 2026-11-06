namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The Seller's currently active subscription, for the Current Plan summary card.
/// </summary>
/// <param name="ScheduledPlanName">
/// Set when a downgrade is scheduled — "Downgrading to [ScheduledPlanName] on
/// [ScheduledPlanEffectiveDate]". Null when nothing is scheduled.
/// </param>
/// <param name="IsTrial">True while this is the free Pro trial, which has no invoice.</param>
/// <param name="TrialEndsAt">When the trial ends and the Seller returns to Free; null otherwise.</param>
/// <param name="Status">
/// <c>past_due</c> from the renewal date until the renewal is paid, even before the hourly job
/// stores it.
/// </param>
/// <param name="CanRenew">True when the Seller can pay the next cycle now (Renew now).</param>
/// <param name="LastGraceDay">While past due: the last day the plan still works unpaid.</param>
/// <param name="RenewalPlanName">The plan the next cycle is for: a scheduled change, or the current plan.</param>
/// <param name="RenewalPrice">What paying the next cycle costs.</param>
/// <param name="PriceVnd"><see cref="Price"/> in whole đồng, as PayOS charges it.</param>
/// <param name="RenewalPriceVnd"><see cref="RenewalPrice"/> in whole đồng.</param>
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
    DateOnly? ScheduledPlanEffectiveDate,
    bool IsTrial = false,
    DateTime? TrialEndsAt = null,
    bool CanRenew = false,
    DateOnly? LastGraceDay = null,
    string? RenewalPlanName = null,
    decimal? RenewalPrice = null,
    decimal PriceVnd = 0m,
    decimal? RenewalPriceVnd = null);
