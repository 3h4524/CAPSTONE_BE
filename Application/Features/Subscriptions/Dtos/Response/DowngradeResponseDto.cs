namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The result of scheduling a downgrade to a lower-tier plan (UC63).
/// </summary>
/// <param name="EffectiveDate">
/// The Seller's current renewal date — the downgrade never applies mid-cycle (BR114).
/// </param>
public sealed record DowngradeResponseDto(
    Guid TargetPlanId,
    string TargetPlanName,
    DateOnly EffectiveDate);
