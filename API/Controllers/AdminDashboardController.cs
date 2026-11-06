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
public sealed class AdminDashboardController(
    IAdminDashboardService dashboardService,
    IAdminOperationsService operationsService) : ControllerBase
{
    /// <summary>
    /// Gets how production went over a Dashboard period: throughput, the pipeline funnel and AI usage.
    /// </summary>
    /// <param name="timeRange">The period: week (7 days), month (30 days), halfyear (6 months) or year (12 months).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The period's production figures.</returns>
    [HttpGet("operations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperationsAsync(
        [FromQuery] string timeRange = "month",
        CancellationToken cancellationToken = default)
    {
        var result = await operationsService.GetOperationsAsync(timeRange, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.ToActionResult(this);
    }

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
