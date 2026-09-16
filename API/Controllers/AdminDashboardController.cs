using APCS.Api.Extensions;
using APCS.Application.Features.Admin;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides endpoints for the Admin Dashboard.
/// </summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminDashboardController(IAdminDashboardService dashboardService) : ControllerBase
{
    /// <summary>
    /// Gets the key metrics for the admin dashboard.
    /// </summary>
    /// <param name="timeRange">The time range (day, month, year).</param>
    /// <param name="date">The reference date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A summary of platform metrics.</returns>
    [HttpGet("metrics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMetricsAsync(
        [FromQuery] string timeRange = "month",
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        var result = await dashboardService.GetMetricsAsync(timeRange, date, cancellationToken);
        
        return result.IsSuccess 
            ? Ok(result.Value) 
            : result.ToActionResult(this);
    }

    /// <summary>
    /// Exports the key metrics as a CSV file.
    /// </summary>
    /// <param name="timeRange">The time range (day, month, year).</param>
    /// <param name="date">The reference date.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A CSV file download.</returns>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportMetricsAsync(
        [FromQuery] string timeRange = "month",
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        var result = await dashboardService.ExportMetricsAsync(timeRange, date, cancellationToken);
        
        if (result.IsFailure)
        {
            return result.ToActionResult(this);
        }

        var fileName = $"Admin_Report_{timeRange}_{date?.ToString("yyyyMMdd") ?? DateTime.UtcNow.ToString("yyyyMMdd")}.csv";
        return File(result.Value, "text/csv", fileName);
    }
}
