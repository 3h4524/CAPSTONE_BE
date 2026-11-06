namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The free Pro trial a Seller just started.
/// </summary>
/// <param name="TrialEndsAt">When the trial ends and the Seller returns to Free.</param>
public sealed record TrialResponseDto(
    Guid SubscriptionId,
    string PlanName,
    DateTime TrialEndsAt);
