namespace APCS.Application.Features.Subscriptions.Dtos.Request;

/// <summary>
/// Schedules the Seller's active paid subscription to move to a lower-tier plan at the start of
/// the next billing cycle.
/// </summary>
/// <param name="PlanId">The lower-tier target plan.</param>
/// <remarks>Use case mapping: UC63 Downgrade Subscription.</remarks>
public sealed record DowngradeRequestDto(Guid PlanId);
