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
}
