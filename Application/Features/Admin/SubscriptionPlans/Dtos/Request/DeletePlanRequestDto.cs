namespace APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;

/// <summary>
/// Confirms deletion of a subscription plan (3.6.10 Delete Subscription Plan).
/// </summary>
/// <param name="Reason">
/// Required by the confirmation dialog's "Reason for deleting this plan" field. There is no
/// dedicated audit-log column for this yet, so it is recorded to the application log only.
/// </param>
public sealed record DeletePlanRequestDto(string Reason);
