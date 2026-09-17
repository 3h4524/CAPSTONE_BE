using APCS.Application.Features.UsageStatistics.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.UsageStatistics;

public interface IUsageStatisticsService
{
    Task<Result<UsageOverviewResponseDto>> GetOverviewAsync(int days, CancellationToken cancellationToken = default);
}
