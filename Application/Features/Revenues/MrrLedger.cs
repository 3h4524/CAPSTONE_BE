using System;
using System.Collections.Generic;
using System.Linq;

namespace APCS.Application.Features.Revenues;

/// <summary>A paid subscription as the MRR figures see it: when it paid, when it stopped, and what it was worth.</summary>
/// <param name="FirstPaidOn">The date of its first paid invoice.</param>
/// <param name="EndedOn">The date it was cancelled, expired or deleted; null while it runs.</param>
/// <param name="RenewalDate">
/// The first day it has not paid for. From then on it is past due and earns nothing until renewed,
/// which matches the "active" status the renewal job keeps.
/// </param>
/// <param name="Segments">Its plan and monthly value over time, oldest first; the first starts on <paramref name="FirstPaidOn"/>.</param>
internal sealed record MrrSubscription(
    Guid UserId,
    DateOnly FirstPaidOn,
    DateOnly? EndedOn,
    DateOnly RenewalDate,
    IReadOnlyList<MrrSegment> Segments);

/// <summary>The plan a subscription was on from <paramref name="From"/>, and its monthly value in VND.</summary>
internal sealed record MrrSegment(DateOnly From, string PlanName, decimal MonthlyValue);

/// <summary>How MRR got from the start of a period to its end.</summary>
/// <remarks>
/// <c>End = Start + New + MovedIn + Expansion - Contraction - MovedOut - Churn</c> always holds.
/// MovedIn and MovedOut are customers changing to or from another plan, so they are zero for all
/// plans together, where a plan change is Expansion or Contraction instead.
/// </remarks>
internal sealed record MrrMovement(
    decimal Start,
    decimal New,
    decimal MovedIn,
    decimal Expansion,
    decimal Contraction,
    decimal MovedOut,
    decimal Churn,
    decimal End);

/// <summary>How a plan's paying customers got from the start of a period to its end.</summary>
/// <remarks>
/// <c>End = Start - SwitchedOut - StoppedPaying + New + SwitchedIn</c> and
/// <c>Start = Stayed + SwitchedOut + StoppedPaying</c> always hold.
/// </remarks>
/// <param name="Stayed">On the plan at both ends.</param>
/// <param name="SwitchedOut">On the plan at the start, paying for another plan at the end.</param>
/// <param name="StoppedPaying">On the plan at the start, paying for nothing at the end.</param>
/// <param name="New">On the plan at the end, paying for nothing at the start.</param>
/// <param name="SwitchedIn">On the plan at the end, paying for another plan at the start.</param>
internal sealed record MrrCustomerFlow(
    int Start,
    int Stayed,
    int SwitchedOut,
    int StoppedPaying,
    int New,
    int SwitchedIn,
    int End)
{
    /// <summary>Customers from the start still paying at the end, on this plan or another.</summary>
    public int Retained => Stayed + SwitchedOut;
}

/// <summary>
/// Monthly recurring revenue on any past day, rebuilt from paid subscriptions, so a period can be
/// compared with the one before it as it really was.
/// </summary>
/// <remarks>
/// Taking today's active subscriptions and filtering them by creation date drops every customer who
/// left since, which understates the past and inflates growth. Here a subscription counts on a day
/// when it had paid by then, had not ended, and had not passed its renewal date unpaid.
/// </remarks>
internal static class MrrLedger
{
    /// <summary>Gets each customer's MRR per plan at the end of <paramref name="day"/>.</summary>
    public static Dictionary<(Guid UserId, string PlanName), decimal> ValueAt(IEnumerable<MrrSubscription> subscriptions, DateOnly day)
    {
        var values = new Dictionary<(Guid, string), decimal>();
        foreach (var subscription in subscriptions)
        {
            if (!IsLiveAt(subscription, day))
            {
                continue;
            }

            var segment = subscription.Segments.Last(s => s.From <= day);
            if (segment.MonthlyValue <= 0)
            {
                continue;
            }

            var key = (subscription.UserId, segment.PlanName);
            values[key] = values.GetValueOrDefault(key) + segment.MonthlyValue;
        }

        return values;
    }

    /// <summary>Gets the total MRR at the end of <paramref name="day"/>.</summary>
    public static decimal TotalAt(IEnumerable<MrrSubscription> subscriptions, DateOnly day) =>
        ValueAt(subscriptions, day).Values.Sum();

    /// <summary>Counts the customers paying at the end of <paramref name="day"/>, once each whatever they hold.</summary>
    public static int CustomersAt(IEnumerable<MrrSubscription> subscriptions, DateOnly day) =>
        ValueAt(subscriptions, day).Keys.Select(key => key.UserId).Distinct().Count();

    /// <summary>
    /// Breaks the change in MRR from the end of the day before <paramref name="from"/> to the end of
    /// <paramref name="to"/> into its causes, for one plan or, when <paramref name="planName"/> is
    /// null, for all plans together.
    /// </summary>
    public static MrrMovement Movement(IReadOnlyList<MrrSubscription> subscriptions, DateOnly from, DateOnly to, string? planName = null)
    {
        var start = ValueAt(subscriptions, from.AddDays(-1));
        var end = ValueAt(subscriptions, to);

        decimal added = 0, movedIn = 0, expansion = 0, contraction = 0, movedOut = 0, churn = 0;
        foreach (var userId in start.Keys.Concat(end.Keys).Select(key => key.UserId).Distinct())
        {
            var (startPlan, startOther) = Split(start, userId, planName);
            var (endPlan, endOther) = Split(end, userId, planName);

            if (startPlan == 0 && endPlan > 0)
            {
                if (startOther > 0) movedIn += endPlan;
                else added += endPlan;
            }
            else if (startPlan > 0 && endPlan == 0)
            {
                if (endOther > 0) movedOut += startPlan;
                else churn += startPlan;
            }
            else if (endPlan > startPlan)
            {
                expansion += endPlan - startPlan;
            }
            else
            {
                contraction += startPlan - endPlan;
            }
        }

        return new MrrMovement(
            Sum(start, planName), added, movedIn, expansion, contraction, movedOut, churn, Sum(end, planName));
    }

    /// <summary>
    /// Follows <paramref name="planName"/>'s paying customers from the end of the day before
    /// <paramref name="from"/> to the end of <paramref name="to"/>, the same days as <see cref="Movement"/>.
    /// </summary>
    public static MrrCustomerFlow CustomerFlow(IReadOnlyList<MrrSubscription> subscriptions, DateOnly from, DateOnly to, string planName)
    {
        var start = ValueAt(subscriptions, from.AddDays(-1)).Keys;
        var end = ValueAt(subscriptions, to).Keys;

        var onPlanAtStart = start.Where(key => key.PlanName == planName).Select(key => key.UserId).ToHashSet();
        var onPlanAtEnd = end.Where(key => key.PlanName == planName).Select(key => key.UserId).ToHashSet();
        var payingAtStart = start.Select(key => key.UserId).ToHashSet();
        var payingAtEnd = end.Select(key => key.UserId).ToHashSet();

        var gone = onPlanAtStart.Where(userId => !onPlanAtEnd.Contains(userId)).ToList();
        var joined = onPlanAtEnd.Where(userId => !onPlanAtStart.Contains(userId)).ToList();
        return new MrrCustomerFlow(
            onPlanAtStart.Count,
            onPlanAtStart.Count(onPlanAtEnd.Contains),
            gone.Count(payingAtEnd.Contains),
            gone.Count(userId => !payingAtEnd.Contains(userId)),
            joined.Count(userId => !payingAtStart.Contains(userId)),
            joined.Count(payingAtStart.Contains),
            onPlanAtEnd.Count);
    }

    /// <summary>Whether the subscription earned MRR at the end of <paramref name="day"/>.</summary>
    public static bool IsLiveAt(MrrSubscription subscription, DateOnly day) =>
        subscription.FirstPaidOn <= day
        && (subscription.EndedOn is null || subscription.EndedOn > day)
        && subscription.RenewalDate > day;

    // A customer's MRR on the plan, and on every other plan. For all plans together the first is
    // their whole MRR and the second is zero.
    private static (decimal Plan, decimal Other) Split(Dictionary<(Guid UserId, string PlanName), decimal> values, Guid userId, string? planName)
    {
        decimal plan = 0, other = 0;
        foreach (var (key, value) in values)
        {
            if (key.UserId != userId) continue;
            if (planName is null || key.PlanName == planName) plan += value;
            else other += value;
        }

        return (plan, other);
    }

    private static decimal Sum(Dictionary<(Guid UserId, string PlanName), decimal> values, string? planName) =>
        values.Where(entry => planName is null || entry.Key.PlanName == planName).Sum(entry => entry.Value);
}
