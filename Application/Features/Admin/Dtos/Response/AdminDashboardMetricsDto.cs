namespace APCS.Application.Features.Admin.Dtos.Response;

/// <summary>
/// Data Transfer Object representing the metrics shown on the Admin Dashboard.
/// </summary>
public sealed record AdminDashboardMetricsDto(
    int TotalUsers,
    int ActivePaidUsers,
    decimal TotalRevenue,
    decimal RecurringRevenue,
    int ActiveBatchJobs,
    int PendingTickets,
    List<MonthlyRevenueDto> RevenueChart,
    List<SupportTicketDto> LatestTickets,
    List<BatchJobDto> RecentBatchJobs
);
