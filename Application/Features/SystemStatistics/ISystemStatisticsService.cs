using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.SystemStatistics.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Features.SystemStatistics;

public interface ISystemStatisticsService
{
    Task<Result<SystemStatisticsOverviewDto>> GetOverviewAsync(StatisticsFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<byte[]>> ExportCsvAsync(StatisticsFilterDto filter, CancellationToken cancellationToken = default);
}
