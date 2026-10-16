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

        var currentInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var previousInvoices = await invoiceRepository.Query()
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= previousStartDate && (i.PaymentDate ?? i.InvoiceDate) <= previousEndDate)
            .ToListAsync(cancellationToken);

        // 1. Total Net Revenue
        decimal currentRevenue = currentInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);
        decimal previousRevenue = previousInvoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);

        // 2. Active Subscribers (from Invoices in period)
        int currentUsers = currentInvoices.Select(i => i.UserId).Distinct().Count();
        int previousUsers = previousInvoices.Select(i => i.UserId).Distinct().Count();

        // 3. ARPU
        decimal currentArpu = currentUsers > 0 ? currentRevenue / currentUsers : 0;
        decimal previousArpu = previousUsers > 0 ? previousRevenue / previousUsers : 0;

        // 4. Subscription MRR (Snapshot of currently active subscriptions)
        // Note: We snapshot current active MRR. Previous MRR is an approximation based on active subs created before filter.EndDate.
        var activeSubscriptions = await subscriptionRepository.Query()
            .Include(s => s.Plan)
            .Where(s => s.Status == "active")
            .ToListAsync(cancellationToken);

        decimal currentMrr = activeSubscriptions.Sum(s => s.Plan.MonthlyPriceUsd);
        decimal previousMrr = activeSubscriptions
            .Where(s => s.CreatedAt <= previousEndDate.ToDateTime(TimeOnly.MaxValue))
            .Sum(s => s.Plan.MonthlyPriceUsd); // Rough estimate for previous

        return Result.Success(new RevenueMetricsDto
        {
            TotalNetRevenue = CreateMetric(currentRevenue, previousRevenue),
            ActiveSubscribers = CreateMetric(currentUsers, previousUsers),
            Arpu = CreateMetric(currentArpu, previousArpu),
            SubscriptionMrr = CreateMetric(currentMrr, previousMrr)
        });
    }

    public async Task<Result<RevenueChartDto>> GetChartsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        var invoices = await invoiceRepository.Query()
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate)
            .ToListAsync(cancellationToken);

        var chartDto = new RevenueChartDto();

        // 1. Trend Chart
        if (filter.Granularity.Equals("Month", StringComparison.OrdinalIgnoreCase))
        {
            chartDto.Trend = invoices
                .GroupBy(i => new { (i.PaymentDate ?? i.InvoiceDate).Year, (i.PaymentDate ?? i.InvoiceDate).Month })
                .Select(g => new RevenueTrendPointDto
                {
                    DateLabel = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                    PlanBreakdown = g
                        .Where(i => i.Subscription?.Plan != null)
                        .GroupBy(i => i.Subscription.Plan.Name)
                        .ToDictionary(
                            pg => pg.Key,
                            pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                        )
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }
        else if (filter.Granularity.Equals("Year", StringComparison.OrdinalIgnoreCase))
        {
            chartDto.Trend = invoices
                .GroupBy(i => (i.PaymentDate ?? i.InvoiceDate).Year)
                .Select(g => new RevenueTrendPointDto
                {
                    DateLabel = g.Key.ToString(),
                    Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                    PlanBreakdown = g
                        .Where(i => i.Subscription?.Plan != null)
                        .GroupBy(i => i.Subscription.Plan.Name)
                        .ToDictionary(
                            pg => pg.Key,
                            pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                        )
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }
        else // Day
        {
            chartDto.Trend = invoices
                .GroupBy(i => (i.PaymentDate ?? i.InvoiceDate))
                .Select(g => new RevenueTrendPointDto
                {
                    DateLabel = g.Key.ToString("yyyy-MM-dd"),
                    Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                    PlanBreakdown = g
                        .Where(i => i.Subscription?.Plan != null)
                        .GroupBy(i => i.Subscription.Plan.Name)
                        .ToDictionary(
                            pg => pg.Key,
                            pg => pg.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd)
                        )
                })
                .OrderBy(x => x.DateLabel)
                .ToList();
        }

        // 2. By Plan Chart
        decimal totalRevenue = invoices.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd);
        chartDto.ByPlan = invoices
            .Where(i => i.Subscription?.Plan != null)
            .GroupBy(i => i.Subscription.Plan.Name)
            .Select(g => new RevenueByPlanDto
            {
                PlanName = g.Key,
                Amount = g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd),
                Percentage = totalRevenue > 0 ? Math.Round((g.Sum(i => i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd) / totalRevenue) * 100, 2) : 0
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

    private static MetricDetail CreateMetric(decimal current, decimal previous)
    {
        return new MetricDetail
        {
            Value = Math.Round(current, 2),
            PreviousValue = Math.Round(previous, 2),
            PercentageChange = previous > 0 
                ? Math.Round(((current - previous) / previous) * 100, 2) 
                : (current > 0 ? 100 : 0)
        };
    }

    public async Task<Result<PagedResult<RevenueInvoiceDto>>> GetInvoicesAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default)
    {
        var query = invoiceRepository.Query()
            .Include(i => i.User)
            .Include(i => i.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(i => i.Status.ToLower() == "paid" && (i.PaymentDate ?? i.InvoiceDate) >= filter.StartDate && (i.PaymentDate ?? i.InvoiceDate) <= filter.EndDate);

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => (i.PaymentDate ?? i.InvoiceDate))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(i => new RevenueInvoiceDto
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                CustomerName = i.User.FullName,
                CustomerEmail = i.User.Email,
                PlanName = i.Subscription.Plan.Name,
                Amount = i.TotalAmount > 0 ? i.TotalAmount : i.AmountUsd,
                Status = i.Status,
                PaymentDate = i.PaymentDate ?? i.InvoiceDate
            })
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<RevenueInvoiceDto>(items, totalCount, filter.Page, filter.PageSize));
    }
}
