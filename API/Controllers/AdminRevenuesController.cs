using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[Route("api/admin/revenues")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminRevenuesController(IRevenueService revenueService) : ControllerBase
{
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(RevenueMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetrics([FromQuery] RevenueFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await revenueService.GetMetricsAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("charts")]
    [ProducesResponseType(typeof(RevenueChartDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCharts([FromQuery] RevenueFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await revenueService.GetChartsAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("invoices")]
    [ProducesResponseType(typeof(APCS.Common.Models.PagedResult<RevenueInvoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoices([FromQuery] RevenueFilterDto filter, CancellationToken cancellationToken)
    {
        var result = await revenueService.GetInvoicesAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
