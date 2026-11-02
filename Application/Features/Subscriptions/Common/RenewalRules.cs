namespace APCS.Application.Features.Subscriptions.Common;

/// <summary>
/// When a paid subscription is due, how long it stays usable unpaid, and when it may be renewed.
/// </summary>
/// <remarks>
/// PayOS only creates one-off payment links, so nothing charges a renewal by itself: the Seller
/// pays each cycle with Renew now. The renewal date is the first day the paid period no longer
/// covers. From that day the subscription is past due and keeps its features for
/// <see cref="GraceDays"/> days; after that it expires and the Seller is back on Free.
/// </remarks>
public static class RenewalRules
{
    /// <summary>Days a past-due subscription keeps its features while waiting for the renewal payment.</summary>
    public const int GraceDays = 3;

    /// <summary>How many days before the renewal date the Seller can already pay the next cycle.</summary>
    public const int RenewWindowDays = 7;

    /// <summary>The last day a subscription with this renewal date is still usable unpaid.</summary>
    public static DateOnly LastGraceDay(DateOnly renewalDate) => renewalDate.AddDays(GraceDays - 1);

    /// <summary>
    /// The earliest renewal date a subscription can have and still be usable on <paramref name="today"/>.
    /// </summary>
    public static DateOnly OldestUsableRenewalDate(DateOnly today) => today.AddDays(1 - GraceDays);

    /// <summary>Whether a paid subscription with this renewal date is past due on <paramref name="today"/>.</summary>
    public static bool IsPastDue(DateOnly renewalDate, DateOnly today) => renewalDate <= today;

    /// <summary>Whether the Seller can pay the next cycle on <paramref name="today"/>.</summary>
    public static bool CanRenew(DateOnly renewalDate, DateOnly today) =>
        renewalDate <= today.AddDays(RenewWindowDays);

    /// <summary>The renewal date after paying one more cycle that starts on <paramref name="periodStart"/>.</summary>
    public static DateOnly NextRenewalDate(DateOnly periodStart, string billingCycle) =>
        billingCycle == "annual" ? periodStart.AddYears(1) : periodStart.AddMonths(1);
}
