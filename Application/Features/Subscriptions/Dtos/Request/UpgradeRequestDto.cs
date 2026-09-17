namespace APCS.Application.Features.Subscriptions.Dtos.Request;

/// <summary>
/// Upgrades the Seller's active paid subscription to a higher-tier plan.
/// </summary>
/// <param name="PlanId">The higher-tier target plan.</param>
public sealed record UpgradeRequestDto(Guid PlanId);
