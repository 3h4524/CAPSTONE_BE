namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// One usage-quota progress indicator on the Subscription Page (BR187, BR189).
/// </summary>
/// <param name="QuotaCode">Stable code identifying the quota, e.g. "image_generation".</param>
/// <param name="Label">Display label, e.g. "Image generation".</param>
/// <param name="Used">Amount consumed in the current billing period.</param>
/// <param name="Limit">The plan's limit for this quota.</param>
/// <param name="Unit">Display unit, e.g. "images", "calls", "GB".</param>
/// <param name="PercentUsed">Used/Limit as a percentage, clamped to [0, 100].</param>
/// <param name="IsWarning">True once usage reaches 100% of the quota (BR189).</param>
public sealed record UsageQuotaDto(
    string QuotaCode,
    string Label,
    decimal Used,
    decimal Limit,
    string Unit,
    decimal PercentUsed,
    bool IsWarning);
