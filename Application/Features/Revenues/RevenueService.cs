using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Revenues;

public sealed class RevenueService(
    IRepository<Invoice> invoiceRepository,
    IRepository<Subscription> subscriptionRepository) : IRevenueService
{
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
        decimal currentRefundAmount = currentRefundedInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);
        decimal previousRefundAmount = previousRefundedInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);

        // 2. Total Revenue (Gross). A refund moves an invoice from "paid" to "refunded", so the paid
        // sum alone already excludes it; gross adds refunded invoices back so Net = Gross - Refunds
        // subtracts each refund exactly once.
        decimal currentTotalRevenue = currentPaidInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd) + currentRefundAmount;
        decimal previousTotalRevenue = previousPaidInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd) + previousRefundAmount;

        // 3. Net Revenue
        decimal currentNetRevenue = currentTotalRevenue - currentRefundAmount;
        decimal previousNetRevenue = previousTotalRevenue - previousRefundAmount;

        // 4. Active Subscribers (from Invoices in period)
        int currentUsers = currentPaidInvoices.Select(i => i.UserId).Distinct().Count();
        int previousUsers = previousPaidInvoices.Select(i => i.UserId).Distinct().Count();

        // 5. ARPU
        decimal currentArpu = currentUsers > 0 ? currentNetRevenue / currentUsers : 0;
        decimal previousArpu = previousUsers > 0 ? previousNetRevenue / previousUsers : 0;

        // 6. Subscription MRR (Snapshot of currently active subscriptions)
        var activeSubscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .Where(s => s.Status == "active")
            .ToListAsync(cancellationToken);

        decimal currentMrr = activeSubscriptions.Sum(s => s.Plan.MonthlyPriceUsd);
        decimal previousMrr = activeSubscriptions
            .Where(s => s.CreatedAt <= previousEndDate.ToDateTime(TimeOnly.MaxValue))
            .Sum(s => s.Plan.MonthlyPriceUsd);

        // 7. ARR = MRR * 12
        decimal currentArr = currentMrr * 12m;
        decimal previousArr = previousMrr * 12m;

        // Generate Sparklines (7 data points spread across filter.StartDate to filter.EndDate)
        var sparklines = GenerateMetricSparklines(filter.StartDate, filter.EndDate, currentPaidInvoices, currentRefundedInvoices, currentMrr, currentUsers);

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
                        .Sum(p => p.TotalAmount > 0 ? p.TotalAmount : p.AmountUsd);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = $"{g.Key.Year}-{g.Key.Month:D2}",
                        Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = g
                            .Where(i => i.Subscription?.Plan != null)
                            .GroupBy(i => i.Subscription.Plan.Name)
                            .ToDictionary(
                                pg => pg.Key,
                                pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                            )
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
                        .Sum(p => p.TotalAmount > 0 ? p.TotalAmount : p.AmountUsd);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = g.Key.ToString(),
                        Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = g
                            .Where(i => i.Subscription?.Plan != null)
                            .GroupBy(i => i.Subscription.Plan.Name)
                            .ToDictionary(
                                pg => pg.Key,
                                pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                            )
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
                        .Sum(p => p.TotalAmount > 0 ? p.TotalAmount : p.AmountUsd);

                    return new RevenueTrendPointDto
                    {
                        DateLabel = g.Key.ToString("yyyy-MM-dd"),
                        Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                        PreviousAmount = prevAmount,
                        PlanBreakdown = g
                            .Where(i => i.Subscription?.Plan != null)
                            .GroupBy(i => i.Subscription.Plan.Name)
                            .ToDictionary(
                                pg => pg.Key,
                                pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                            )
                    };
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }

        // 2. By Plan Chart
        decimal totalRevenue = invoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);
        var previousByPlan = previousInvoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd));

        var allSubscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .ToListAsync(cancellationToken);
        var (subscriptionRecords, _) = await LoadLifecycleRecordsAsync(allSubscriptions, cancellationToken);

        chartDto.ByPlan = invoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .Select(g =>
            {
                decimal amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);
                decimal previousAmount = previousByPlan.GetValueOrDefault(g.Key);
                int customers = g.Select(i => i.UserId).Distinct().Count();
                var retention = PlanMetrics.Retention(subscriptionRecords, g.Key, filter.StartDate, filter.EndDate);
                return new RevenueByPlanDto
                {
                    PlanName = g.Key,
                    Amount = amount,
                    Percentage = totalRevenue > 0 ? Math.Round(amount / totalRevenue * 100, 2) : 0,
                    PurchaseCount = g.Count(),
                    UniqueCustomers = customers,
                    Arpu = PlanMetrics.Arpu(amount, customers),
                    RetentionBase = retention.ActiveAtStart,
                    RetentionRate = retention.Rate,
                    PreviousAmount = previousAmount,
                    PercentageChange = RevenueMath.PercentageChange(amount, previousAmount)
                };
            })
            .OrderByDescending(x => x.Amount)
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
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd));

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

    private static MetricSparklines GenerateMetricSparklines(
        DateOnly startDate,
        DateOnly endDate,
        List<Invoice> paidInvoices,
        List<Invoice> refundedInvoices,
        decimal currentMrr,
        int currentUsers)
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
                .Sum(x => x.TotalAmount > 0 ? x.TotalAmount : x.AmountUsd);

            var refundBucket = refundedInvoices
                .Where(x => (x.PaymentDate ?? x.InvoiceDate) >= bucketStart && (x.PaymentDate ?? x.InvoiceDate) <= bucketEnd)
                .Sum(x => x.TotalAmount > 0 ? x.TotalAmount : x.AmountUsd);

            // Gross = still-paid + refunded (see GetMetricsAsync), so net is the still-paid amount.
            var grossBucket = paidBucket + refundBucket;
            var netBucket = grossBucket - refundBucket;

            totalRevList.Add(Math.Round(grossBucket, 2));
            netRevList.Add(Math.Round(netBucket, 2));
            refundList.Add(Math.Round(refundBucket, 2));

            decimal factor = 0.85m + (0.15m * (i + 1) / pointsCount);
            mrrList.Add(Math.Round(currentMrr * factor, 2));
            arrList.Add(Math.Round(currentMrr * 12m * factor, 2));

            decimal userFactor = 0.8m + (0.2m * (i + 1) / pointsCount);
            usersList.Add(Math.Max(0, Math.Round(currentUsers * userFactor)));
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
            Amount = r.Amount,
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
        var allSubscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .Include(s => s.ScheduledPlan)
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

        decimal refundedAmount = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "refunded" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .SumAsync(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd, cancellationToken);

        var stuckBefore = DateTime.UtcNow - FinancialAlertRules.StuckCheckoutAge;
        var stuckCheckouts = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "issued" && i.CreatedAt != null && i.CreatedAt < stuckBefore)
            .Select(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
            .ToListAsync(cancellationToken);

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

        // 2. Revenue Forecast
        decimal currentMrr = allSubscriptions
            .Where(s => s.Status.ToLower() == "active" && s.Plan != null)
            .Sum(s => s.Plan.MonthlyPriceUsd);

        var forecastDto = await BuildWeeklyForecastAsync(today, cancellationToken);
        forecastDto.CurrentMrr = currentMrr;

        // 3. Financial Alerts - see FinancialAlertRules for when each one is raised
        static decimal Paid(Invoice i) => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd;
        static decimal MonthlyValue(Subscription s) =>
            string.Equals(s.BillingCycle, "annual", StringComparison.OrdinalIgnoreCase) && s.AnnualPriceUsd is { } annual ? annual / 12m : s.MonthlyPriceUsd;
        static decimal CycleAmount(Subscription s) =>
            string.Equals(s.BillingCycle, "annual", StringComparison.OrdinalIgnoreCase) && s.AnnualPriceUsd is { } annual ? annual : s.MonthlyPriceUsd;

        var liveSubscriptions = allSubscriptions
            .Where(s => s.Status.ToLower() == "active" && s.DeletedAt == null)
            .ToList();
        var lapsed = liveSubscriptions.Where(s => s.RenewalDate < today).ToList();
        var renewalsDue = liveSubscriptions
            .Where(s => s.RenewalDate >= today && s.RenewalDate <= today.AddDays(FinancialAlertRules.RenewalWindowDays))
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

        var alerts = FinancialAlertRules.Evaluate(new FinancialAlertInputs
        {
            LapsedCount = lapsed.Count,
            LapsedMonthlyValue = lapsed.Sum(MonthlyValue),
            LapsedOldestDays = lapsed.Count > 0 ? today.DayNumber - lapsed.Min(s => s.RenewalDate).DayNumber : 0,
            StuckCheckoutCount = stuckCheckouts.Count,
            StuckCheckoutAmount = stuckCheckouts.Sum(),
            DowngradeCount = downgrades.Count,
            DowngradeMonthlyLoss = downgrades.Sum(s => Math.Max(0m, s.Plan.MonthlyPriceUsd - s.ScheduledPlan!.MonthlyPriceUsd)),
            RenewalsDueCount = renewalsDue.Count,
            RenewalsDueAmount = renewalsDue.Sum(CycleAmount),
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
            ForecastRSquared = forecastDto.RSquared
        });

        return Result.Success(new RevenueInsightsDto
        {
            SubscriptionAnalytics = subAnalytics,
            Forecast = forecastDto,
            FinancialAlerts = alerts
        });
    }
}
