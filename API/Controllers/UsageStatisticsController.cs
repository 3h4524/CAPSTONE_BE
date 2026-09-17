using APCS.Api.Extensions;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.UsageStatistics.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/usage")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class UsageStatisticsController(IUsageStatisticsService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(UsageOverviewResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        (await service.GetOverviewAsync(days, cancellationToken)).ToActionResult(this);
}
