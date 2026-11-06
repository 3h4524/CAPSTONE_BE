using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.UsageStatistics;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Revenues;

/// <remarks>
/// Invoices and plan prices are stored in USD and PayOS charges them in VND at
/// <see cref="PaymentGatewaySettings.UsdToVndRate"/>, so every money figure leaves this service in
/// VND at that same rate, matching what customers actually paid.
/// </remarks>
public sealed class RevenueService(
    IRepository<Invoice> invoiceRepository,
    IRepository<Subscription> subscriptionRepository,
    IRepository<SubscriptionPlan> planRepository,
    IRepository<ApiUsageRecord> usageRecordRepository,
    IRepository<DesignImage> designImageRepository,
    IRepository<MockupImage> mockupRepository,
    IRepository<PromoVideo> videoRepository,
    IOptions<PaymentGatewaySettings> gatewaySettings) : IRevenueService
{
    /// <summary>Months the MRR Movement chart shows, ending with the period's month.</summary>
    private const int MovementMonths = 6;

    public async Task<Result<RevenueMetricsDto>> GetMetricsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        int days = (filter.EndDate.ToDateTime(TimeOnly.MinValue) - filter.StartDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var previousStartDate = filter.StartDate.AddDays(-days);
        var previousEndDate = filter.EndDate.AddDays(-days);

        var currentPaidInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var previousPaidInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= previousStartDate && (i.PaymentDate ?? i.InvoiceDate) <= previousEndDate)
            .ToListAsync(cancellationToken);

        var currentRefundedInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "refunded" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var previousRefundedInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "refunded" && (i.PaymentDate ?? i.InvoiceDate) >= previousStartDate && (i.PaymentDate ?? i.InvoiceDate) <= previousEndDate)
            .ToListAsync(cancellationToken);

        // 1. Refund Amount
        decimal currentRefundAmount = currentRefundedInvoices.Sum(Paid);
        decimal previousRefundAmount = previousRefundedInvoices.Sum(Paid);

        // 2. Total Revenue (Gross). A refund moves an invoice from "paid" to "refunded", so the paid
        // sum alone already excludes it; gross adds refunded invoices back so Net = Gross - Refunds
        // subtracts each refund exactly once.
        decimal currentTotalRevenue = currentPaidInvoices.Sum(Paid) + currentRefundAmount;
        decimal previousTotalRevenue = previousPaidInvoices.Sum(Paid) + previousRefundAmount;

        // 3. Net Revenue
        decimal currentNetRevenue = currentTotalRevenue - currentRefundAmount;
        decimal previousNetRevenue = previousTotalRevenue - previousRefundAmount;

        // 4. ARPU: revenue is spread over the customers who paid in the period, cancelled or not.
        int currentPayingUsers = currentPaidInvoices.Select(i => i.UserId).Distinct().Count();
        int previousPayingUsers = previousPaidInvoices.Select(i => i.UserId).Distinct().Count();
        decimal currentArpu = currentPayingUsers > 0 ? currentNetRevenue / currentPayingUsers : 0;
        decimal previousArpu = previousPayingUsers > 0 ? previousNetRevenue / previousPayingUsers : 0;

        // 5. MRR and Active Subscribers at the end of each period, rebuilt from paid subscriptions
        // (see MrrLedger), so the previous period keeps the customers who have left since. With the
        // period ending today they match the paid plan counts on the Subscription Plans page.
        var mrrSubscriptions = await LoadMrrSubscriptionsAsync(cancellationToken);
        decimal currentMrr = MrrLedger.TotalAt(mrrSubscriptions, filter.EndDate);
        decimal previousMrr = MrrLedger.TotalAt(mrrSubscriptions, previousEndDate);
        int currentUsers = MrrLedger.CustomersAt(mrrSubscriptions, filter.EndDate);
        int previousUsers = MrrLedger.CustomersAt(mrrSubscriptions, previousEndDate);

        // 6. ARR = MRR * 12
        decimal currentArr = currentMrr * 12m;
        decimal previousArr = previousMrr * 12m;

        var sparklines = GenerateMetricSparklines(filter.StartDate, filter.EndDate, currentPaidInvoices, currentRefundedInvoices, mrrSubscriptions);

        return Result.Success(new RevenueMetricsDto
        {
            TotalRevenue = CreateMetric(currentTotalRevenue, previousTotalRevenue, sparklines.TotalRevenue),
            NetRevenue = CreateMetric(currentNetRevenue, previousNetRevenue, sparklines.NetRevenue),
            SubscriptionMrr = CreateMetric(currentMrr, previousMrr, sparklines.Mrr),
            Arr = CreateMetric(currentArr, previousArr, sparklines.Arr),
            ActiveSubscribers = CreateMetric(currentUsers, previousUsers, sparklines.ActiveSubscribers),
            RefundAmount = CreateMetric(currentRefundAmount, previousRefundAmount, sparklines.RefundAmount),
            Arpu = CreateMetric(currentArpu, previousArpu)
        });
    }

    public async Task<Result<RevenueChartDto>> GetChartsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        int days = (filter.EndDate.ToDateTime(TimeOnly.MinValue) - filter.StartDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var previousStartDate = filter.StartDate.AddDays(-days);
        var previousEndDate = filter.EndDate.AddDays(-days);

        var invoices = await invoiceRepository.Query()
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var previousInvoices = await invoiceRepository.Query()
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= previousStartDate && (i.PaymentDate ?? i.InvoiceDate) <= previousEndDate)
            .ToListAsync(cancellationToken);

        var chartDto = new RevenueChartDto();

        // 1. Trend Chart
        if (filter.Granularity.Equals("Month", StringComparison.OrdinalIgnoreCase))
        {
            chartDto.Trend = invoices
                .GroupBy(i => new { (i.PaymentDate ?? i.InvoiceDate).Year, (i.PaymentDate ?? i.InvoiceDate).Month })
                .Select(g =>
                {
                    var pointDate = new DateOnly(g.Key.Year, g.Key.Month, 1);
                    var prevPointDate = pointDate.AddDays(-days);

                    var prevAmount = previousInvoices
                        .Where(p => {
                            var d = p.PaymentDate ?? p.InvoiceDate;
                            return (d.Year == g.Key.Year - 1 && d.Month == g.Key.Month)
                                || (d.Year == prevPointDate.Year && d.Month == prevPointDate.Month);
                        })
                        .Sum(Paid);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = $"{g.Key.Year}-{g.Key.Month:D2}",
                        Amount = g.Sum(Paid),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = PlanBreakdown(g)
                    };
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }
        else if (filter.Granularity.Equals("Year", StringComparison.OrdinalIgnoreCase))
        {
            chartDto.Trend = invoices
                .GroupBy(i => (i.PaymentDate ?? i.InvoiceDate).Year)
                .Select(g =>
                {
                    var prevAmount = previousInvoices
                        .Where(p => (p.PaymentDate ?? p.InvoiceDate).Year == g.Key - 1)
                        .Sum(Paid);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = g.Key.ToString(),
                        Amount = g.Sum(Paid),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = PlanBreakdown(g)
                    };
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }
        else // Day
        {
            chartDto.Trend = invoices
                .GroupBy(i => (i.PaymentDate ?? i.InvoiceDate))
                .Select(g =>
                {
                    var prevAmount = previousInvoices
                        .Where(p => (p.PaymentDate ?? p.InvoiceDate) == g.Key.AddDays(-days))
                        .Sum(Paid);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = g.Key.ToString("yyyy-MM-dd"),
                        Amount = g.Sum(Paid),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = PlanBreakdown(g)
                    };
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }

        // 2. By Plan Chart
        decimal totalRevenue = invoices.Sum(Paid);
        var revenueByPlan = invoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .ToDictionary(g => g.Key, g => g.ToList());
        var previousByPlan = previousInvoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .ToDictionary(g => g.Key, g => g.Sum(Paid));

        // A plan is listed when it earned in the period or carried MRR at either end of it, so a
        // plan whose customers all left still shows where its MRR went.
        var mrrSubscriptions = await LoadMrrSubscriptionsAsync(cancellationToken);
        var mrrAtStart = MrrLedger.ValueAt(mrrSubscriptions, filter.StartDate.AddDays(-1));
        var mrrAtEnd = MrrLedger.ValueAt(mrrSubscriptions, filter.EndDate);
        var planNames = revenueByPlan.Keys
            .Concat(mrrAtStart.Keys.Select(key => key.PlanName))
            .Concat(mrrAtEnd.Keys.Select(key => key.PlanName))
            .Distinct()
            .ToList();

        var signals = await BuildUsageSignalsAsync(mrrSubscriptions, planNames, filter.StartDate, filter.EndDate, cancellationToken);

        chartDto.ByPlan = planNames
            .Select(planName =>
            {
                var planInvoices = revenueByPlan.GetValueOrDefault(planName) ?? [];
                decimal amount = planInvoices.Sum(Paid);
                decimal previousAmount = previousByPlan.GetValueOrDefault(planName);
                int customers = planInvoices.Select(i => i.UserId).Distinct().Count();
                // Retention follows the same customers, on the same days, as the MRR above.
                var flow = MrrLedger.CustomerFlow(mrrSubscriptions, filter.StartDate, filter.EndDate, planName);
                decimal mrrStart = PlanTotal(mrrAtStart, planName);
                decimal mrrEnd = PlanTotal(mrrAtEnd, planName);
                var signal = signals[planName];

                return new RevenueByPlanDto
                {
                    PlanName = planName,
                    Amount = amount,
                    Percentage = totalRevenue > 0 ? Math.Round(amount / totalRevenue * 100, 2) : 0,
                    PurchaseCount = planInvoices.Count,
                    UniqueCustomers = customers,
                    Arpu = PlanMetrics.Arpu(amount, customers),
                    Customers = new PlanCustomerFlowDto
                    {
                        Start = flow.Start,
                        Stayed = flow.Stayed,
                        SwitchedOut = flow.SwitchedOut,
                        StoppedPaying = flow.StoppedPaying,
                        New = flow.New,
                        SwitchedIn = flow.SwitchedIn,
                        End = flow.End
                    },
                    RetentionRate = PlanMetrics.RetentionRate(flow),
                    PreviousAmount = previousAmount,
                    PercentageChange = RevenueMath.PercentageChange(amount, previousAmount),
                    MrrStart = mrrStart,
                    MrrEnd = mrrEnd,
                    MrrChangePercent = RevenueMath.PercentageChange(mrrEnd, mrrStart),
                    UsageSignal = ToSignalDto(signal)
                };
            })
            .OrderByDescending(x => x.Amount)
            .ThenByDescending(x => x.MrrEnd)
            .ToList();

        // 3. Available Plans
        chartDto.AvailablePlans = invoices
            .Where(i => i.Subscription?.Plan != null)
            .Select(i => i.Subscription.Plan.Name)
            .Distinct()
            .ToList();

        return Result.Success(chartDto);
    }

    /// <summary>
    /// Projects subscriptions and every paid invoice into the records the lifecycle metrics work on,
    /// attaching each subscription's first payment date and plan name.
    /// </summary>
    private async Task<(List<SubscriptionRecord> Subscriptions, List<PaidInvoiceRecord> PaidInvoices)> LoadLifecycleRecordsAsync(
        IEnumerable<Subscription> subscriptions,
        CancellationToken cancellationToken)
    {
        var paidInvoiceRecords = (await invoiceRepository.Query()
                .Where(i => i.Status.ToLower() == "paid")
                .Select(i => new { i.Id, i.SubscriptionId, PaidOn = i.PaymentDate ?? i.InvoiceDate })
                .ToListAsync(cancellationToken))
            .Select(i => new PaidInvoiceRecord(i.Id, i.SubscriptionId, i.PaidOn))
            .ToList();

        var firstPaidOn = paidInvoiceRecords
            .GroupBy(i => i.SubscriptionId)
            .ToDictionary(g => g.Key, g => g.Min(i => i.PaidOn));

        var subscriptionRecords = subscriptions
            .Select(s => new SubscriptionRecord(
                s.Id,
                s.UserId,
                s.CreatedAt ?? s.StartDate.ToDateTime(TimeOnly.MinValue),
                s.CancelledAt,
                firstPaidOn.TryGetValue(s.Id, out var paidOn) ? paidOn : null,
                s.Plan?.Name))
            .ToList();

        return (subscriptionRecords, paidInvoiceRecords);
    }

    /// <summary>
    /// Builds every paid subscription's MRR history (see <see cref="MrrLedger"/>), in VND.
    /// </summary>
    /// <remarks>
    /// A subscription is worth its stored price, monthly or annual / 12, from its first payment. A
    /// paid renewal can switch it to its scheduled plan in place, so each paid renewal starts a new
    /// segment at the price it billed, and the plan before the first one is the one that renewal
    /// recorded. Renewals billed before that was recorded fall back to the current plan.
    /// </remarks>
    private async Task<List<MrrSubscription>> LoadMrrSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .Where(s => s.TrialEndsAt == null)
            .ToListAsync(cancellationToken);

        var paidBySubscription = (await invoiceRepository.Query()
                .Where(i => i.Status.ToLower() == "paid")
                .Select(i => new { i.SubscriptionId, PaidOn = i.PaymentDate ?? i.InvoiceDate, i.TotalAmount, i.AmountUsd, i.Items })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.SubscriptionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.PaidOn).ToList());

        var plans = await planRepository.Query().ToDictionaryAsync(plan => plan.Id, cancellationToken);

        var result = new List<MrrSubscription>();
        foreach (var subscription in subscriptions)
        {
            if (!paidBySubscription.TryGetValue(subscription.Id, out var paid) || subscription.Plan is null)
            {
                continue;
            }

            var renewals = paid
                .Select(invoice => (invoice.PaidOn, Amount: invoice.TotalAmount > 0 ? invoice.TotalAmount : invoice.AmountUsd,
                    Item: RenewalInvoiceItem.TryRead(invoice.Items)))
                .Where(invoice => invoice.Item is not null)
                .ToList();

            var firstRenewal = renewals.FirstOrDefault().Item;
            var initialPlanId = firstRenewal?.FromPlanId ?? subscription.PlanId;
            var initialCycle = firstRenewal?.FromBillingCycle ?? subscription.BillingCycle;
            var initialPlan = plans.GetValueOrDefault(initialPlanId) ?? subscription.Plan;
            var initialValue = initialPlanId == subscription.PlanId
                ? MonthlyValue(subscription.MonthlyPriceUsd, subscription.AnnualPriceUsd, initialCycle)
                : MonthlyValue(initialPlan.MonthlyPriceUsd, initialPlan.AnnualPriceUsd, initialCycle);

            var segments = new List<MrrSegment> { new(paid[0].PaidOn, initialPlan.Name, Vnd(initialValue)) };
            foreach (var (paidOn, amount, item) in renewals)
            {
                var plan = plans.GetValueOrDefault(item!.PlanId) ?? subscription.Plan;
                var monthly = IsAnnual(item.BillingCycle) ? amount / 12m : amount;
                segments.Add(new MrrSegment(paidOn, plan.Name, Vnd(monthly)));
            }

            var endedAt = subscription.CancelledAt ?? subscription.DeletedAt;
            result.Add(new MrrSubscription(
                subscription.UserId,
                paid[0].PaidOn,
                endedAt is { } ended ? DateOnly.FromDateTime(ended) : null,
                subscription.RenewalDate,
                segments));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PlanCohortDto>>> GetPlanCohortsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var mrrSubscriptions = await LoadMrrSubscriptionsAsync(cancellationToken);
        var planNames = MrrLedger.ValueAt(mrrSubscriptions, from.AddDays(-1)).Keys
            .Select(key => key.PlanName)
            .Distinct();

        IReadOnlyList<PlanCohortDto> cohorts = planNames
            .Select(planName =>
            {
                var (stayed, left) = PlanUsageSignals.Groups(mrrSubscriptions, from, to, planName);
                var leftCustomers = left
                    .Select(userId =>
                    {
                        var (firstPaidOn, leftOn) = PlanUsageSignals.Tenure(mrrSubscriptions, userId, to);
                        return new LeftCustomerDto(userId, firstPaidOn, leftOn ?? to);
                    })
                    .ToList();
                return new PlanCohortDto(planName, stayed.ToList(), leftCustomers);
            })
            .ToList();

        return Result.Success(cohorts);
    }

    private sealed record UsageSignal(int Stayed, int Left, PlanUsageGap? Gap);

    /// <summary>Compares, for each plan, what its customers who left used with what those who stayed used.</summary>
    private async Task<Dictionary<string, UsageSignal>> BuildUsageSignalsAsync(
        IReadOnlyList<MrrSubscription> mrrSubscriptions,
        IEnumerable<string> planNames,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var groups = planNames.ToDictionary(
            planName => planName,
            planName => PlanUsageSignals.Groups(mrrSubscriptions, from, to, planName));

        // Usage is only read when some plan has enough customers on both sides to compare.
        var comparable = groups.Values.Any(group =>
            group.Stayed.Count >= PlanUsageSignals.MinimumGroup && group.Left.Count >= PlanUsageSignals.MinimumGroup);
        var usage = comparable
            ? await LoadFeatureUsersAsync(from.AddDays(-PlanUsageSignals.UsageLookbackDays), to, cancellationToken)
            : [];

        return groups.ToDictionary(
            entry => entry.Key,
            entry => new UsageSignal(
                entry.Value.Stayed.Count,
                entry.Value.Left.Count,
                PlanUsageSignals.LargestGap(entry.Value.Stayed, entry.Value.Left, usage)));
    }

    /// <summary>The Sellers who used each feature from <paramref name="from"/> to the end of <paramref name="to"/>.</summary>
    private async Task<List<(string Feature, string Label, IReadOnlySet<Guid> Users)>> LoadFeatureUsersAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        async Task<IReadOnlySet<Guid>> Users(IQueryable<Guid> userIds) =>
            (await userIds.Distinct().ToListAsync(cancellationToken)).ToHashSet();

        IQueryable<MockupImage> Mockups() => mockupRepository.Query()
            .Where(mockup => mockup.DeletedAt == null && mockup.CreatedAt >= start && mockup.CreatedAt < end);
        IQueryable<PromoVideo> Videos(string mode) => videoRepository.Query()
            .Where(video => video.Mode == mode && video.DeletedAt == null && video.CreatedAt >= start && video.CreatedAt < end);

        return
        [
            ("design_generation", "Design generation", await Users(designImageRepository.Query()
                .Where(image => image.DeletedAt == null && image.CreatedAt >= start && image.CreatedAt < end)
                .Select(image => image.Product.UserId))),
            ("listing_generation", "Listing generation", await Users(usageRecordRepository.Query()
                .Where(record => record.Feature == UsageFeatures.Listing && record.CreatedAt >= start && record.CreatedAt < end)
                .Select(record => record.UserId))),
            ("rendered_mockups", "Rendered mockups", await Users(Mockups()
                .Where(mockup => mockup.SourceType == MockupSources.Generated)
                .Select(mockup => mockup.Product.UserId))),
            ("lifestyle_mockups", "Lifestyle mockups", await Users(Mockups()
                .Where(mockup => mockup.SourceType == MockupSources.Uploaded && mockup.ApiUsageRecordId != null && mockup.Role == "Lifestyle")
                .Select(mockup => mockup.Product.UserId))),
            ("studio_keyframes", "AI Studio keyframes", await Users(Mockups()
                .Where(mockup => mockup.SourceType == MockupSources.AiGenerated)
                .Select(mockup => mockup.Product.UserId))),
            ("standard_videos", "Standard videos", await Users(Videos("standard").Select(video => video.Product.UserId))),
            ("ai_videos", "AI videos", await Users(Videos("ai_shot").Select(video => video.Product.UserId))),
        ];
    }

    private static PlanUsageSignalDto ToSignalDto(UsageSignal signal) => new()
    {
        Stayed = signal.Stayed,
        Left = signal.Left,
        MinimumGroup = PlanUsageSignals.MinimumGroup,
        HasSignal = signal.Gap is not null,
        Feature = signal.Gap?.Feature,
        FeatureLabel = signal.Gap?.Label,
        StayedRate = signal.Gap?.StayedRate,
        LeftRate = signal.Gap?.LeftRate
    };

    /// <summary>Weeks of history the forecast regression may look back over.</summary>
    private const int ForecastLookbackWeeks = 26;

    /// <summary>Weeks projected ahead; covers the 12-month option of the chart's horizon selector.</summary>
    private const int ForecastHorizonWeeks = 52;

    /// <summary>
    /// Builds the revenue forecast from collected (paid) revenue grouped into Monday-to-Sunday weeks.
    /// The current week is excluded because it has not finished, and the series starts at the first
    /// week with a payment so weeks before the business took revenue do not drag the trend to zero.
    /// </summary>
    private async Task<RevenueForecastDto> BuildWeeklyForecastAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var currentWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var windowStart = currentWeekStart.AddDays(-7 * ForecastLookbackWeeks);

        var paidInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= windowStart && (i.PaymentDate ?? i.InvoiceDate) < currentWeekStart)
            .ToListAsync(cancellationToken);

        var revenueByWeek = paidInvoices
            .GroupBy(i => ((i.PaymentDate ?? i.InvoiceDate).DayNumber - windowStart.DayNumber) / 7)
            .ToDictionary(g => g.Key, g => g.Sum(Paid));

        int firstWeek = revenueByWeek.Count > 0 ? revenueByWeek.Keys.Min() : ForecastLookbackWeeks;
        var weekStarts = Enumerable.Range(firstWeek, ForecastLookbackWeeks - firstWeek)
            .Select(w => windowStart.AddDays(7 * w))
            .ToList();
        var weeklyRevenue = Enumerable.Range(firstWeek, ForecastLookbackWeeks - firstWeek)
            .Select(w => revenueByWeek.GetValueOrDefault(w))
            .ToList();

        var fit = LinearRevenueForecast.Fit(weeklyRevenue, ForecastHorizonWeeks);

        var points = weekStarts
            .Select((weekStart, index) => new ForecastPointDto
            {
                MonthLabel = FormatWeekLabel(weekStart),
                IsHistorical = true,
                Actual = Math.Round(weeklyRevenue[index], 2)
            })
            .ToList();

        decimal lastActual = weeklyRevenue.LastOrDefault();
        foreach (var step in fit.Steps)
        {
            if (step.WeekOffset == 0)
            {
                // Anchor the projected lines on the last observed week so the chart joins them up.
                var anchor = points[^1];
                anchor.ForecastBase = step.Base;
                anchor.Optimistic = step.Optimistic;
                anchor.Conservative = step.Conservative;
                continue;
            }

            points.Add(new ForecastPointDto
            {
                MonthLabel = FormatWeekLabel(currentWeekStart.AddDays(7 * (step.WeekOffset - 1))),
                IsHistorical = false,
                ForecastBase = step.Base,
                Optimistic = step.Optimistic,
                Conservative = step.Conservative,
                PercentageChangeVsCurrent = lastActual > 0 ? Math.Round((step.Base - lastActual) / lastActual * 100m, 1) : 0m
            });
        }

        decimal meanWeekly = weeklyRevenue.Count > 0 ? weeklyRevenue.Average() : 0m;
        decimal weeklyTrend = Math.Round((decimal)fit.Slope, 2);

        return new RevenueForecastDto
        {
            Points = points,
            Status = fit.Status,
            SampleWeeks = fit.SampleSize,
            RSquared = Math.Round((decimal)fit.RSquared, 4),
            WeeklyTrend = weeklyTrend,
            ProjectedGrowthRate = meanWeekly > 0 ? Math.Round(weeklyTrend / meanWeekly * 100m, 1) : 0m
        };
    }

    private static string FormatWeekLabel(DateOnly weekStart) =>
        weekStart.ToString("MMM dd", System.Globalization.CultureInfo.InvariantCulture);

    private static MetricDetail CreateMetric(decimal current, decimal previous, List<decimal>? sparkline = null)
    {
        return new MetricDetail
        {
            Value = Math.Round(current, 2),
            PreviousValue = Math.Round(previous, 2),
            PercentageChange = RevenueMath.PercentageChange(current, previous),
            Sparkline = sparkline ?? new List<decimal>()
        };
    }

    private sealed record MetricSparklines(
        List<decimal> TotalRevenue,
        List<decimal> NetRevenue,
        List<decimal> Mrr,
        List<decimal> Arr,
        List<decimal> ActiveSubscribers,
        List<decimal> RefundAmount);

    private MetricSparklines GenerateMetricSparklines(
        DateOnly startDate,
        DateOnly endDate,
        List<Invoice> paidInvoices,
        List<Invoice> refundedInvoices,
        IReadOnlyList<MrrSubscription> mrrSubscriptions)
    {
        const int pointsCount = 7;
        var startDt = startDate.ToDateTime(TimeOnly.MinValue);
        var endDt = endDate.ToDateTime(TimeOnly.MinValue);
        var totalDays = Math.Max(1, (endDt - startDt).Days);
        var stepDays = Math.Max(1, totalDays / (pointsCount - 1));

        var totalRevList = new List<decimal>();
        var netRevList = new List<decimal>();
        var mrrList = new List<decimal>();
        var arrList = new List<decimal>();
        var usersList = new List<decimal>();
        var refundList = new List<decimal>();

        for (int i = 0; i < pointsCount; i++)
        {
            var bucketStart = startDate.AddDays(i * stepDays);
            var bucketEnd = (i == pointsCount - 1) ? endDate : startDate.AddDays((i + 1) * stepDays - 1);
            if (bucketStart > endDate) bucketStart = endDate;
            if (bucketEnd > endDate) bucketEnd = endDate;

            var paidBucket = paidInvoices
                .Where(x => (x.PaymentDate ?? x.InvoiceDate) >= bucketStart && (x.PaymentDate ?? x.InvoiceDate) <= bucketEnd)
                .Sum(Paid);

            var refundBucket = refundedInvoices
                .Where(x => (x.PaymentDate ?? x.InvoiceDate) >= bucketStart && (x.PaymentDate ?? x.InvoiceDate) <= bucketEnd)
                .Sum(Paid);

            // Gross = still-paid + refunded (see GetMetricsAsync), so net is the still-paid amount.
            var grossBucket = paidBucket + refundBucket;
            var netBucket = grossBucket - refundBucket;

            totalRevList.Add(Math.Round(grossBucket, 2));
            netRevList.Add(Math.Round(netBucket, 2));
            refundList.Add(Math.Round(refundBucket, 2));

            // MRR and subscribers are read off the ledger at each bucket's end, so the line is their real history.
            decimal mrr = MrrLedger.TotalAt(mrrSubscriptions, bucketEnd);
            mrrList.Add(Math.Round(mrr, 2));
            arrList.Add(Math.Round(mrr * 12m, 2));
            usersList.Add(MrrLedger.CustomersAt(mrrSubscriptions, bucketEnd));
        }

        return new MetricSparklines(totalRevList, netRevList, mrrList, arrList, usersList, refundList);
    }

    public async Task<Result<PagedResult<RevenueInvoiceDto>>> GetInvoicesAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        // Every invoice status is listed (the table filters by status); revenue figures elsewhere stay paid-only.
        var query = invoiceRepository.Query()
            .Include(i => i.User)
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(i =>
                i.InvoiceNumber.ToLower().Contains(search)
                || i.User.FullName.ToLower().Contains(search)
                || i.User.Email.ToLower().Contains(search)
                // A user id is matched as text, so a pasted full id or a copied fragment of one both work.
                || i.UserId.ToString().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Plan))
        {
            query = query.Where(i => i.Subscription.Plan.Name == filter.Plan);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var dbStatus = ToInvoiceDbStatus(filter.Status);
            query = query.Where(i => i.Status.ToLower() == dbStatus);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(i => i.PaymentDate ?? i.InvoiceDate)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(i => new
            {
                i.Id,
                i.InvoiceNumber,
                i.UserId,
                CustomerName = i.User.FullName,
                CustomerEmail = i.User.Email,
                PlanName = i.Subscription.Plan.Name,
                Amount = i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd,
                i.Status,
                PaymentDate = i.PaymentDate ?? i.InvoiceDate,
                i.CreatedAt,
                i.PayosOrderCode,
                i.StripeInvoiceId
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new RevenueInvoiceDto
        {
            Id = r.Id,
            InvoiceNumber = r.InvoiceNumber,
            UserId = r.UserId,
            CustomerName = r.CustomerName,
            CustomerEmail = r.CustomerEmail,
            PlanName = r.PlanName,
            Amount = Vnd(r.Amount),
            Status = ToDisplayStatus(r.Status),
            PaymentDate = r.PaymentDate,
            CreatedAt = r.CreatedAt,
            PaymentMethod = r.PayosOrderCode != null ? "PayOS" : r.StripeInvoiceId != null ? "Stripe" : null
        }).ToList();

        return Result.Success(new PagedResult<RevenueInvoiceDto>(items, totalCount, filter.Page, filter.PageSize));
    }

    // The schema's "issued" invoice is awaiting payment and "void" is an abandoned or declined checkout.
    private static string ToDisplayStatus(string dbStatus) => dbStatus.ToLowerInvariant() switch
    {
        "issued" => "pending",
        "void" => "cancelled",
        var other => other
    };

    private static string ToInvoiceDbStatus(string displayStatus) => displayStatus.Trim().ToLowerInvariant() switch
    {
        "pending" => "issued",
        "cancelled" => "void",
        var other => other
    };

    public async Task<Result<RevenueInsightsDto>> GetInsightsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        int days = (filter.EndDate.ToDateTime(TimeOnly.MinValue) - filter.StartDate.ToDateTime(TimeOnly.MinValue)).Days + 1;
        var previousStartDate = filter.StartDate.AddDays(-days);
        var previousEndDate = filter.EndDate.AddDays(-days);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Fetch subscriptions and invoices needed
        // Free trials are left out of every subscription metric: they were never paid for, and
        // one ending is not a cancellation.
        var allSubscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .Include(s => s.ScheduledPlan)
            .Where(s => s.TrialEndsAt == null)
            .ToListAsync(cancellationToken);

        var currentPaidInvoices = await invoiceRepository.Query()
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var previousPaidInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= previousStartDate && (i.PaymentDate ?? i.InvoiceDate) <= previousEndDate)
            .ToListAsync(cancellationToken);

        // The schema has no "failed" invoice: a PayOS decline or an expired link voids the invoice and
        // expires its subscription (a Seller's own cancel voids it but cancels the subscription), and
        // "overdue" is the schema's value for a renewal that was not paid.
        int failedPaymentCount = await invoiceRepository.Query()
            .Where(i => i.InvoiceDate >= filter.StartDate && i.InvoiceDate <= filter.EndDate
                && (i.Status.ToLower() == "overdue"
                    || (i.Status.ToLower() == "void" && i.Subscription != null && i.Subscription.Status.ToLower() == "expired")))
            .CountAsync(cancellationToken);

        decimal refundedAmount = Vnd(await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "refunded" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .SumAsync(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd, cancellationToken));

        var stuckBefore = DateTime.UtcNow - FinancialAlertRules.StuckCheckoutAge;
        var stuckCheckouts = (await invoiceRepository.Query()
                .Where(i => i.Status.ToLower() == "issued" && i.CreatedAt != null && i.CreatedAt < stuckBefore)
                .Select(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                .ToListAsync(cancellationToken))
            .Select(Vnd)
            .ToList();

        // 1. Subscription Analytics - see SubscriptionMetrics for what counts as new, renewed and churned
        var (subscriptionRecords, paidInvoiceRecords) = await LoadLifecycleRecordsAsync(allSubscriptions, cancellationToken);

        var points = new List<SubscriptionAnalyticsPointDto>();
        int bucketStep = Math.Max(1, days / 15);
        for (var date = filter.StartDate; date <= filter.EndDate; date = date.AddDays(bucketStep))
        {
            var nextDate = date.AddDays(bucketStep - 1);
            if (nextDate > filter.EndDate) nextDate = filter.EndDate;

            points.Add(new SubscriptionAnalyticsPointDto
            {
                DateLabel = date.ToString("MMM dd", System.Globalization.CultureInfo.InvariantCulture),
                NewSubscriptions = SubscriptionMetrics.CountNew(subscriptionRecords, date, nextDate),
                Renewals = SubscriptionMetrics.CountRenewals(paidInvoiceRecords, date, nextDate),
                Cancellations = SubscriptionMetrics.CountChurned(subscriptionRecords, date, nextDate)
            });

            if (nextDate == filter.EndDate) break;
        }

        int currentNewSubs = SubscriptionMetrics.CountNew(subscriptionRecords, filter.StartDate, filter.EndDate);
        int prevNewSubs = SubscriptionMetrics.CountNew(subscriptionRecords, previousStartDate, previousEndDate);
        int currentRenewals = SubscriptionMetrics.CountRenewals(paidInvoiceRecords, filter.StartDate, filter.EndDate);
        int prevRenewals = SubscriptionMetrics.CountRenewals(paidInvoiceRecords, previousStartDate, previousEndDate);
        int currentCancels = SubscriptionMetrics.CountChurned(subscriptionRecords, filter.StartDate, filter.EndDate);
        int prevCancels = SubscriptionMetrics.CountChurned(subscriptionRecords, previousStartDate, previousEndDate);

        int activeAtStart = SubscriptionMetrics.CountActiveAt(subscriptionRecords, filter.StartDate);
        decimal? currentChurn = SubscriptionMetrics.MonthlyChurnRate(subscriptionRecords, filter.StartDate, filter.EndDate);
        decimal? prevChurn = SubscriptionMetrics.MonthlyChurnRate(subscriptionRecords, previousStartDate, previousEndDate);

        // A rate is compared in percentage points: 5% -> 6% is +1 pp, where a relative change would
        // report +20% and read as if a fifth of the customers had left. An undefined rate on either
        // side has nothing to compare against.
        var churnMetric = CreateMetric(currentChurn ?? 0m, prevChurn ?? 0m);
        churnMetric.PercentageChange = currentChurn is { } cur && prevChurn is { } prev ? cur - prev : null;

        var subAnalytics = new SubscriptionAnalyticsDto
        {
            Points = points,
            NewSubscriptions = CreateMetric(currentNewSubs, prevNewSubs),
            Renewals = CreateMetric(currentRenewals, prevRenewals),
            Cancellations = CreateMetric(currentCancels, prevCancels),
            ChurnRate = churnMetric,
            HasChurnRate = currentChurn is not null,
            ActiveAtPeriodStart = activeAtStart
        };

        // 2. MRR Movement - see MrrLedger for how a customer's MRR change is classified
        var mrrSubscriptions = await LoadMrrSubscriptionsAsync(cancellationToken);
        var mrrMovement = BuildMrrMovement(mrrSubscriptions, filter.StartDate, filter.EndDate);

        // 3. Revenue Forecast
        var forecastDto = await BuildWeeklyForecastAsync(today, cancellationToken);
        forecastDto.CurrentMrr = MrrLedger.TotalAt(mrrSubscriptions, today);

        // 4. Financial Alerts - see FinancialAlertRules for when each one is raised
        decimal MonthlyValueVnd(Subscription s) => Vnd(MonthlyValue(s.MonthlyPriceUsd, s.AnnualPriceUsd, s.BillingCycle));
        decimal CycleAmountVnd(Subscription s) =>
            Vnd(IsAnnual(s.BillingCycle) && s.AnnualPriceUsd is { } annual ? annual : s.MonthlyPriceUsd);

        var liveSubscriptions = allSubscriptions
            .Where(s => s.Status.ToLower() == "active" && s.DeletedAt == null)
            .ToList();
        // Past due: stored as past_due by SubscriptionRenewalJob, or still "active" until its next run.
        var lapsed = allSubscriptions
            .Where(s => s.DeletedAt == null
                && (s.Status.ToLower() == "past_due" || s.Status.ToLower() == "active")
                && RenewalRules.IsPastDue(s.RenewalDate, today)
                && s.RenewalDate >= RenewalRules.OldestUsableRenewalDate(today))
            .ToList();
        var renewalsDue = liveSubscriptions
            .Where(s => s.RenewalDate > today && s.RenewalDate <= today.AddDays(FinancialAlertRules.RenewalWindowDays))
            .ToList();
        var downgrades = liveSubscriptions
            .Where(s => s.ScheduledPlan != null && s.Plan != null
                && s.ScheduledPlanEffectiveDate <= today.AddDays(FinancialAlertRules.DowngradeWindowDays))
            .ToList();
        var topPlan = currentPaidInvoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .Select(g => new { Name = g.Key, Total = g.Sum(Paid) })
            .OrderByDescending(x => x.Total)
            .FirstOrDefault();

        var planBridges = mrrMovement.Period.Where(bridge => bridge.PlanName is not null).ToList();
        var signals = await BuildUsageSignalsAsync(
            mrrSubscriptions, planBridges.Select(bridge => bridge.PlanName!), filter.StartDate, filter.EndDate, cancellationToken);

        var alerts = FinancialAlertRules.Evaluate(new FinancialAlertInputs
        {
            LapsedCount = lapsed.Count,
            LapsedMonthlyValue = lapsed.Sum(MonthlyValueVnd),
            LapsedOldestDays = lapsed.Count > 0 ? today.DayNumber - lapsed.Min(s => s.RenewalDate).DayNumber : 0,
            StuckCheckoutCount = stuckCheckouts.Count,
            StuckCheckoutAmount = stuckCheckouts.Sum(),
            DowngradeCount = downgrades.Count,
            DowngradeMonthlyLoss = downgrades.Sum(s => Vnd(Math.Max(0m, s.Plan.MonthlyPriceUsd - s.ScheduledPlan!.MonthlyPriceUsd))),
            RenewalsDueCount = renewalsDue.Count,
            RenewalsDueAmount = renewalsDue.Sum(CycleAmountVnd),
            CurrentRevenue = currentPaidInvoices.Sum(Paid),
            PreviousRevenue = previousPaidInvoices.Sum(Paid),
            CurrentChurn = currentChurn,
            PreviousChurn = prevChurn,
            FailedPaymentCount = failedPaymentCount,
            PaidInvoiceCount = currentPaidInvoices.Count,
            RefundedAmount = refundedAmount,
            TopPlanName = topPlan?.Name,
            TopPlanRevenue = topPlan?.Total ?? 0m,
            ForecastStatus = forecastDto.Status,
            ForecastWeeklyTrend = forecastDto.WeeklyTrend,
            ForecastRSquared = forecastDto.RSquared,
            PlanMrr = planBridges
                .Select(bridge => new PlanMrrInput(bridge.PlanName!, bridge.Start, bridge.End, signals[bridge.PlanName!].Left))
                .ToList(),
            UsageGaps = signals
                .Where(entry => entry.Value.Gap is not null)
                .Select(entry => (entry.Key, entry.Value.Gap!))
                .ToList()
        });

        return Result.Success(new RevenueInsightsDto
        {
            SubscriptionAnalytics = subAnalytics,
            Forecast = forecastDto,
            FinancialAlerts = alerts,
            MrrMovement = mrrMovement
        });
    }

    /// <summary>
    /// Builds the MRR Movement card: the calendar months up to the period's end, and the period itself
    /// for all plans together and for each plan that had MRR at either end of it.
    /// </summary>
    private static MrrMovementDto BuildMrrMovement(IReadOnlyList<MrrSubscription> mrrSubscriptions, DateOnly from, DateOnly to)
    {
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        var months = Enumerable.Range(0, MovementMonths)
            .Select(offset => lastMonth.AddMonths(offset - (MovementMonths - 1)))
            .Select(monthStart =>
            {
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var movement = MrrLedger.Movement(mrrSubscriptions, monthStart, monthEnd < to ? monthEnd : to);
                return new MrrMovementMonthDto
                {
                    MonthStart = monthStart.ToString("yyyy-MM-dd"),
                    New = movement.New,
                    Expansion = movement.Expansion,
                    Contraction = movement.Contraction,
                    Churn = movement.Churn,
                    Net = movement.New + movement.Expansion - movement.Contraction - movement.Churn
                };
            })
            .ToList();

        var planNames = MrrLedger.ValueAt(mrrSubscriptions, from.AddDays(-1)).Keys
            .Concat(MrrLedger.ValueAt(mrrSubscriptions, to).Keys)
            .Select(key => key.PlanName)
            .Distinct();

        var period = new[] { ToBridge(null, MrrLedger.Movement(mrrSubscriptions, from, to)) }
            .Concat(planNames
                .Select(planName => ToBridge(planName, MrrLedger.Movement(mrrSubscriptions, from, to, planName)))
                .OrderByDescending(bridge => bridge.End)
                .ThenByDescending(bridge => bridge.Start))
            .ToList();

        return new MrrMovementDto { Months = months, Period = period };
    }

    private static MrrBridgeDto ToBridge(string? planName, MrrMovement movement) => new()
    {
        PlanName = planName,
        Start = movement.Start,
        New = movement.New,
        MovedIn = movement.MovedIn,
        Expansion = movement.Expansion,
        Contraction = movement.Contraction,
        MovedOut = movement.MovedOut,
        Churn = movement.Churn,
        End = movement.End
    };

    private Dictionary<string, decimal> PlanBreakdown(IEnumerable<Invoice> invoices) =>
        invoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .ToDictionary(pg => pg.Key, pg => pg.Sum(Paid));

    private static decimal PlanTotal(Dictionary<(Guid UserId, string PlanName), decimal> values, string planName) =>
        values.Where(entry => entry.Key.PlanName == planName).Sum(entry => entry.Value);

    private static bool IsAnnual(string? billingCycle) =>
        string.Equals(billingCycle, "annual", StringComparison.OrdinalIgnoreCase);

    // An annual price is spread over its twelve months; a plan with no annual price bills monthly.
    private static decimal MonthlyValue(decimal monthlyPrice, decimal? annualPrice, string? billingCycle) =>
        IsAnnual(billingCycle) && annualPrice is { } annual ? annual / 12m : monthlyPrice;

    // A price in cents times a whole-đồng rate is already whole, so nothing is rounded here.
    private decimal Vnd(decimal usd) => usd * gatewaySettings.Value.UsdToVndRate;

    private decimal Paid(Invoice invoice) => Vnd(invoice.TotalAmount > 0 ? invoice.TotalAmount : invoice.AmountUsd);
}
