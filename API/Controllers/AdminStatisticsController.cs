using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.SystemStatistics;
using APCS.Application.Features.SystemStatistics.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[Route("api/admin/statistics")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminStatisticsController(ISystemStatisticsService statisticsService) : ControllerBase
{
    [HttpGet("overview")]
    [ProducesResponseType(typeof(SystemStatisticsOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] StatisticsFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await statisticsService.GetOverviewAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] StatisticsFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await statisticsService.ExportCsvAsync(filter, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        string fileName = $"system_statistics_{filter.TimeRange ?? "30d"}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(result.Value, "text/csv; charset=utf-8", fileName);
    }
}
