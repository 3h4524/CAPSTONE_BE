namespace APCS.Application.Features.Admin.Dtos.Response;

/// <summary>
/// Data Transfer Object representing the metrics shown on the Admin Dashboard. Money is in VND,
/// computed as on the Revenue page.
/// </summary>
public sealed record AdminDashboardMetricsDto(
    int TotalUsers,
    int ActivePaidUsers,
    decimal TotalRevenue,
    decimal RecurringRevenue,
    int ActiveBatchJobs,
    int PendingTickets,
    List<SupportTicketDto> LatestTickets,
    List<BatchJobDto> RecentBatchJobs,
    int TotalBatchJobs = 0
);
