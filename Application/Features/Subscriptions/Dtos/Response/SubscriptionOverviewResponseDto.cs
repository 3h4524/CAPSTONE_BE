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
public sealed record SubscriptionOverviewResponseDto(
    CurrentSubscriptionDto? CurrentSubscription,
    IReadOnlyList<UsageQuotaDto> UsageQuotas,
    IReadOnlyList<AvailablePlanDto> AvailablePlans,
    IReadOnlyList<RecentInvoiceDto> RecentInvoices,
    bool HasActivePaidPlan);
