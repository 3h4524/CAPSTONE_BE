using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Admin;

/// <summary>
/// Implements the admin dashboard use cases.
/// </summary>
public sealed class AdminDashboardService(
    IRepository<User> userRepository,
    IRepository<Subscription> subscriptionRepository,
    IRepository<Invoice> invoiceRepository,
    IRepository<BatchJob> batchJobRepository,
    IRepository<SupportTicket> ticketRepository,
    TimeProvider timeProvider,
    ILogger<AdminDashboardService> logger)
    : IAdminDashboardService
{
    /// <inheritdoc />
    public async Task<Result<AdminDashboardMetricsDto>> GetMetricsAsync(string timeRange = "month", DateTime? date = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = date ?? timeProvider.GetUtcNow().DateTime;
            var referenceDate = new DateOnly(now.Year, now.Month, now.Day);

            DateOnly startDate, endDate;
            if (timeRange.ToLower() == "day")
            {
                startDate = referenceDate;
                endDate = referenceDate;
            }
            else if (timeRange.ToLower() == "year")
            {
                startDate = new DateOnly(referenceDate.Year, 1, 1);
                endDate = new DateOnly(referenceDate.Year, 12, 31);
            }
            else
            {
                startDate = new DateOnly(referenceDate.Year, referenceDate.Month, 1);
                endDate = new DateOnly(referenceDate.Year, referenceDate.Month, DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month));
            }

            // 1. Total Users
            var totalUsers = await userRepository.Query().CountAsync(cancellationToken);

            // 2. Active Paid Users (distinct users with active subscriptions)
            var activePaidUsers = await subscriptionRepository.Query()
                .Where(s => s.Status.ToLower() == "active")
                .Select(s => s.UserId)
                .Distinct()
                .CountAsync(cancellationToken);

            // 3. Total Revenue
            var totalRevenue = await invoiceRepository.Query()
                .Where(i => i.Status.ToLower() == "paid" && i.InvoiceDate >= startDate && i.InvoiceDate <= endDate)
                .SumAsync(i => i.TotalAmount, cancellationToken);

            // 4. Monthly Recurring Revenue (MRR)
            var monthlyRecurringRevenue = await subscriptionRepository.Query()
                .Where(s => s.Status.ToLower() == "active")
                .SumAsync(s => s.MonthlyPriceUsd, cancellationToken);

            // 5. Active Batch Jobs (running or queued)
            var activeBatchJobs = await batchJobRepository.Query()
                .Where(j => j.Status.ToLower() == "running" || j.Status.ToLower() == "queued" || j.Status.ToLower() == "pending")
                .CountAsync(cancellationToken);

            // 6. Pending Tickets (open or pending)
            var pendingTickets = await ticketRepository.Query()
                .Where(t => t.Status.ToLower() == "open" || t.Status.ToLower() == "pending")
                .CountAsync(cancellationToken);

            // 7. Revenue Chart
            var revenueChart = new List<MonthlyRevenueDto>();
            if (timeRange.ToLower() == "day")
            {
                var sevenDaysAgo = referenceDate.AddDays(-6);
                var invoicesLast7Days = await invoiceRepository.Query()
                    .Where(i => i.Status.ToLower() == "paid" && i.InvoiceDate >= sevenDaysAgo && i.InvoiceDate <= referenceDate)
                    .ToListAsync(cancellationToken);

                for (int i = 6; i >= 0; i--)
                {
                    var dayDate = referenceDate.AddDays(-i);
                    var dayStr = dayDate.ToString("ddd");
                    var dayInvoices = invoicesLast7Days.Where(inv => inv.InvoiceDate == dayDate).ToList();
                    revenueChart.Add(new MonthlyRevenueDto(dayStr, dayInvoices.Sum(inv => inv.TotalAmount), 0m));
                }
            }
            else if (timeRange.ToLower() == "year")
            {
                var fiveYearsAgo = new DateOnly(referenceDate.Year - 4, 1, 1);
                var invoicesLast5Years = await invoiceRepository.Query()
                    .Where(i => i.Status.ToLower() == "paid" && i.InvoiceDate >= fiveYearsAgo && i.InvoiceDate <= endDate)
                    .ToListAsync(cancellationToken);

                for (int i = 4; i >= 0; i--)
                {
                    var yearDate = referenceDate.Year - i;
                    var yearStr = yearDate.ToString();
                    var yearInvoices = invoicesLast5Years.Where(inv => inv.InvoiceDate.Year == yearDate).ToList();
                    revenueChart.Add(new MonthlyRevenueDto(yearStr, yearInvoices.Sum(inv => inv.TotalAmount), 0m));
                }
            }
            else
            {
                var sixMonthsAgo = startDate.AddMonths(-5);
                var invoicesLast6Months = await invoiceRepository.Query()
                    .Where(i => i.Status.ToLower() == "paid" && i.InvoiceDate >= sixMonthsAgo && i.InvoiceDate <= endDate)
                    .ToListAsync(cancellationToken);

                for (int i = 5; i >= 0; i--)
                {
                    var monthDate = startDate.AddMonths(-i);
                    var monthStr = monthDate.ToString("MMM");
                    var monthInvoices = invoicesLast6Months.Where(inv => inv.InvoiceDate.Year == monthDate.Year && inv.InvoiceDate.Month == monthDate.Month).ToList();

                    revenueChart.Add(new MonthlyRevenueDto(monthStr, monthInvoices.Sum(inv => inv.TotalAmount), 0m));
                }
            }

            // 8. Latest Tickets (Top 5)
            var latestTickets = await ticketRepository.Query()
                .Include(t => t.User)
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .Select(t => new SupportTicketDto(
                    t.User.FullName,
                    t.User.Email,
                    $"#{t.TicketNumber} · {t.Subject}",
                    t.Status,
                    t.Priority
                ))
                .ToListAsync(cancellationToken);

            // 9. Recent Batch Jobs (Top 5)
            var recentBatchJobs = await batchJobRepository.Query()
                .Include(b => b.User)
                .OrderByDescending(b => b.CreatedAt)
                .Take(5)
                .Select(b => new BatchJobDto(
                    b.Name,
                    b.TotalProducts ?? 0,
                    b.Status,
                    b.ProgressPercentage.HasValue ? $"{b.ProgressPercentage.Value:0}%" : "0%",
                    b.User.FullName,
                    b.Status.ToLower() == "completed" ? "emerald" :
                        b.Status.ToLower() == "running" ? "amber" :
                        b.Status.ToLower() == "queued" ? "orange" : "rose"
                ))
                .ToListAsync(cancellationToken);

            var metrics = new AdminDashboardMetricsDto(
                totalUsers,
                activePaidUsers,
                totalRevenue,
                monthlyRecurringRevenue,
                activeBatchJobs,
                pendingTickets,
                revenueChart,
                latestTickets,
                recentBatchJobs
            );

            return Result.Success(metrics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while getting admin dashboard metrics.");
            return Result.Failure<AdminDashboardMetricsDto>(new Error("AdminDashboard.Error", "Failed to retrieve dashboard metrics.", ErrorType.Failure));
        }
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> ExportMetricsAsync(string timeRange = "month", DateTime? date = null, CancellationToken cancellationToken = default)
    {
        var metricsResult = await GetMetricsAsync(timeRange, date, cancellationToken);
        if (metricsResult.IsFailure)
        {
            return Result.Failure<byte[]>(metricsResult.Error);
        }

        var metrics = metricsResult.Value;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Admin Dashboard Export");
        sb.AppendLine($"Time Range,{timeRange}");
        sb.AppendLine($"Date,{date?.ToString("yyyy-MM-dd") ?? "Now"}");
        sb.AppendLine();
        sb.AppendLine("--- Summary Metrics ---");
        sb.AppendLine($"Total Users,{metrics.TotalUsers}");
        sb.AppendLine($"Active Paid Users,{metrics.ActivePaidUsers}");
        sb.AppendLine($"Total Revenue,{metrics.TotalRevenue}");
        sb.AppendLine($"Recurring Revenue,{metrics.RecurringRevenue}");
        sb.AppendLine($"Active Batch Jobs,{metrics.ActiveBatchJobs}");
        sb.AppendLine($"Pending Tickets,{metrics.PendingTickets}");
        sb.AppendLine();

        sb.AppendLine("--- Latest Support Tickets ---");
        sb.AppendLine("User Name,User Email,Issue,Status,Priority");
        foreach (var t in metrics.LatestTickets)
        {
            sb.AppendLine($"\"{t.UserName}\",\"{t.UserEmail}\",\"{t.Issue}\",\"{t.Status}\",\"{t.Priority}\"");
        }
        sb.AppendLine();

        sb.AppendLine("--- Recent Batch Jobs ---");
        sb.AppendLine("Name,Items,Status,Progress,User Name");
        foreach (var j in metrics.RecentBatchJobs)
        {
            sb.AppendLine($"\"{j.Name}\",{j.ItemCount},\"{j.Status}\",\"{j.ProgressPercentage}\",\"{j.UserName}\"");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return Result.Success(bytes);
    }
}
