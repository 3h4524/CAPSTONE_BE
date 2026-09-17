using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin;

/// <summary>
/// Service interface for retrieving Admin Dashboard data.
/// </summary>
public interface IAdminDashboardService
{
    /// <summary>
    /// Gets the key metrics for the admin dashboard based on a timeframe.
    /// </summary>
    /// <param name="timeRange">The time range filter ("day", "month", "year"). Default is "month".</param>
    /// <param name="date">The reference date. Defaults to UTC now.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A summary of platform metrics.</returns>
    Task<Result<AdminDashboardMetricsDto>> GetMetricsAsync(string timeRange = "month", DateTime? date = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the current dashboard metrics to a CSV byte array.
    /// </summary>
    /// <param name="timeRange">The time range filter.</param>
    /// <param name="date">The reference date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A CSV string represented as a byte array.</returns>
    Task<Result<byte[]>> ExportMetricsAsync(string timeRange = "month", DateTime? date = null, CancellationToken cancellationToken = default);
}
