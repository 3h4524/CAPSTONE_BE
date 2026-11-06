using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Common.Models;

namespace APCS.Application.Features.Revenues;

public interface IRevenueService
{
    Task<Result<RevenueMetricsDto>> GetMetricsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<RevenueChartDto>> GetChartsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<RevenueInvoiceDto>>> GetInvoicesAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<RevenueInsightsDto>> GetInsightsAsync(RevenueFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Splits each plan's customers at <paramref name="from"/> into those who stayed and those who left by <paramref name="to"/>.</summary>
    Task<Result<IReadOnlyList<PlanCohortDto>>> GetPlanCohortsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
