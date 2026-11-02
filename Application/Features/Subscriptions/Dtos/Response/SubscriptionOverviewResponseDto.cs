namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// The full payload for the Subscription Page (UC58): current plan, usage quotas, available
/// plans, and recent invoices.
/// </summary>
/// <param name="CurrentSubscription">Null when the Seller has never subscribed to anything yet.</param>
/// <param name="HasActivePaidPlan">
/// True when the Seller already has a non-zero-priced active subscription (BR190) — the client
/// uses this to decide whether a plan card's CTA opens checkout or shows MSG57 instead.
/// </param>
/// <param name="TrialEligible">True when the Seller has no paid plan and never started a trial.</param>
/// <param name="TrialDays">How long the free trial lasts.</param>
/// <param name="TrialPlanName">The plan the free trial gives, as an Admin set it; null when it is not on sale.</param>
/// <param name="TrialAllowances">What the free trial includes over its whole length, as an Admin set it.</param>
public sealed record SubscriptionOverviewResponseDto(
    CurrentSubscriptionDto? CurrentSubscription,
    IReadOnlyList<UsageQuotaDto> UsageQuotas,
    IReadOnlyList<AvailablePlanDto> AvailablePlans,
    IReadOnlyList<RecentInvoiceDto> RecentInvoices,
    bool HasActivePaidPlan,
    bool TrialEligible = false,
    int TrialDays = 0,
    string? TrialPlanName = null,
    IReadOnlyList<TrialAllowanceDto>? TrialAllowances = null);

/// <summary>One thing the free trial counts, such as "mockups", and how many it includes.</summary>
public sealed record TrialAllowanceDto(string Code, string Label, int Limit);
