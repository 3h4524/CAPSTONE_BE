using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.TestReports;
using APCS.Application.Features.TestReports.Dtos;
using APCS.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Admin endpoints for the test runs: what ran, how it went, and the button that starts a run.
/// </summary>
[ApiController]
[Route("api/admin/test-reports")]
[Authorize(Roles = "Admin,Super Admin")]
public sealed class AdminTestReportsController(ITestReportService testReportService) : ControllerBase
{
    /// <summary>
    /// Gets a paginated and filtered list of test runs, newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TestReportRunDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRuns(
        [FromQuery] TestReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await testReportService.ListAsync(filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// Gets one test run with the tests it covered.
    /// </summary>
    [HttpGet("{runId}")]
    [ProducesResponseType(typeof(TestReportDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(string runId, CancellationToken cancellationToken)
    {
        var result = await testReportService.GetAsync(runId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>
    /// Starts a test run and returns it right away, while it is still going.
    /// </summary>
    [HttpPost("runs")]
    [ProducesResponseType(typeof(TestReportRunDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartRun(CancellationToken cancellationToken)
    {
        var result = await testReportService.StartRunAsync(CurrentUserEmail(), cancellationToken);
        if (result.IsSuccess)
        {
            return Accepted(result.Value);
        }

        return result.Error.Type == Common.Models.ErrorType.Conflict
            ? Conflict(result.Error)
            : BadRequest(result.Error);
    }

    /// <summary>
    /// Removes a test run and its report.
    /// </summary>
    [HttpDelete("{runId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRun(string runId, CancellationToken cancellationToken)
    {
        var result = await testReportService.DeleteRunAsync(runId, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    private string? CurrentUserEmail() => User.FindFirst(ClaimTypes.Email)?.Value;
}
