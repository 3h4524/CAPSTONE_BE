using System;
using System.Collections.Generic;
using System.Linq;

namespace APCS.Application.Features.Revenues;

/// <summary>A subscription as seen by the revenue analytics.</summary>
/// <param name="FirstPaidOn">The date of its first paid invoice; null when it was never paid.</param>
/// <param name="PlanName">The subscribed plan's name, when known.</param>
internal sealed record SubscriptionRecord(
    Guid Id,
    Guid UserId,
    DateTime CreatedAt,
    DateTime? CancelledAt,
    DateOnly? FirstPaidOn,
    string? PlanName = null);

/// <summary>A paid invoice as seen by the revenue analytics.</summary>
internal sealed record PaidInvoiceRecord(Guid Id, Guid SubscriptionId, DateOnly PaidOn);

/// <summary>
/// Subscription lifecycle counts for the revenue dashboard.
/// </summary>
/// <remarks>
/// <para>
/// Only subscriptions that were ever paid count. Checkout creates a subscription row before payment,
/// and an abandoned or declined checkout marks that row cancelled, so counting raw rows reports
/// every unpaid checkout as both a signup and a cancellation.
/// </para>
/// <para>
/// A plan change is neither a signup nor a cancellation. Buying a plan cancels the current one at
/// checkout and an upgrade retires the old one once the new one is paid; in both cases the newer paid
/// subscription replaces the older one. "Replaces" means the newer one was created while the older
/// one was still live, allowing <see cref="SwitchWindow"/> for the two writes not being simultaneous.
/// </para>
/// </remarks>
internal static class SubscriptionMetrics
{
    public static readonly TimeSpan SwitchWindow = TimeSpan.FromHours(1);

    /// <summary>Counts paid subscriptions first paid in the range that do not replace another plan.</summary>
    public static int CountNew(IReadOnlyList<SubscriptionRecord> subscriptions, DateOnly from, DateOnly to) =>
        subscriptions.Count(s =>
            s.FirstPaidOn is { } paidOn && paidOn >= from && paidOn <= to
            && !subscriptions.Any(older => Replaces(s, older)));

    /// <summary>Counts paid subscriptions cancelled in the range that no newer paid plan replaced.</summary>
    public static int CountChurned(IReadOnlyList<SubscriptionRecord> subscriptions, DateOnly from, DateOnly to) =>
        subscriptions.Count(s =>
            s.FirstPaidOn is not null
            && s.CancelledAt is { } cancelledAt
            && DateOnly.FromDateTime(cancelledAt) >= from && DateOnly.FromDateTime(cancelledAt) <= to
            && !subscriptions.Any(newer => Replaces(newer, s)));

    /// <summary>
    /// Counts customers with a live paid subscription at the start of <paramref name="date"/>. Counted
    /// per user so the brief overlap during a plan change is not counted twice.
    /// </summary>
    public static int CountActiveAt(IReadOnlyList<SubscriptionRecord> subscriptions, DateOnly date) =>
        subscriptions
            .Where(s => IsLiveAt(s, date))
            .Select(s => s.UserId)
            .Distinct()
            .Count();

    /// <summary>Whether the subscription was paid and not yet cancelled at the start of <paramref name="date"/>.</summary>
    public static bool IsLiveAt(SubscriptionRecord subscription, DateOnly date) =>
        subscription.FirstPaidOn is { } paidOn && paidOn < date
        && (subscription.CancelledAt is null || subscription.CancelledAt >= date.ToDateTime(TimeOnly.MinValue));

    /// <summary>Counts paid invoices in the range that are not their subscription's first payment.</summary>
    public static int CountRenewals(IReadOnlyList<PaidInvoiceRecord> paidInvoices, DateOnly from, DateOnly to)
    {
        var firstPaymentIds = paidInvoices
            .GroupBy(i => i.SubscriptionId)
            .Select(g => g.OrderBy(i => i.PaidOn).ThenBy(i => i.Id).First().Id)
            .ToHashSet();

        return paidInvoices.Count(i => i.PaidOn >= from && i.PaidOn <= to && !firstPaymentIds.Contains(i.Id));
    }

    /// <summary>
    /// Gets the average monthly churn rate in percent: cancellations in the range over the
    /// customer-months at risk.
    /// </summary>
    /// <remarks>
    /// Dividing every cancellation of a long range by the customers active on its first day counts
    /// customers who joined and left inside the range against a base that never included them, and
    /// adds up month after month, so a growing business sees rates above 100%. Instead the range is
    /// cut at calendar months; each piece contributes the customers active at its start, weighted by
    /// the share of its month it covers. For a range of exactly one month this is cancellations over
    /// customers active at its start.
    /// </remarks>
    /// <returns>null when nobody was active at the start of any month in the range, where churn is undefined.</returns>
    public static decimal? MonthlyChurnRate(IReadOnlyList<SubscriptionRecord> subscriptions, DateOnly from, DateOnly to)
    {
        decimal customerMonths = 0;
        for (var start = from; start <= to;)
        {
            var monthEnd = new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            var end = monthEnd < to ? monthEnd : to;
            decimal monthShare = (end.DayNumber - start.DayNumber + 1) / (decimal)DateTime.DaysInMonth(start.Year, start.Month);
            customerMonths += CountActiveAt(subscriptions, start) * monthShare;
            start = end.AddDays(1);
        }

        return customerMonths > 0
            ? Math.Round(CountChurned(subscriptions, from, to) * 100m / customerMonths, 1)
            : null;
    }

    private static bool Replaces(SubscriptionRecord newer, SubscriptionRecord older) =>
        newer.Id != older.Id
        && newer.UserId == older.UserId
        && newer.FirstPaidOn is not null
        && older.FirstPaidOn is not null
        && newer.CreatedAt > older.CreatedAt
        && (older.CancelledAt is null || older.CancelledAt >= newer.CreatedAt - SwitchWindow);
}
