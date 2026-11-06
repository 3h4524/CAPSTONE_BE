using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin;

/// <summary>
/// Reads how the platform's production went, for the Admin Dashboard.
/// </summary>
public interface IAdminOperationsService
{
    /// <summary>
    /// Gets production, pipeline and AI figures for a Dashboard period.
    /// </summary>
    /// <param name="timeRange">"week" (the last 7 days) or "month" (the last 30 days), by day; "halfyear" (the last 6 months) or "year" (the last 12 months), by month.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<AdminOperationsDto>> GetOperationsAsync(string timeRange, CancellationToken cancellationToken = default);
}
