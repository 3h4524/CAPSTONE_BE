using APCS.Application.Features.SellerDashboard.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.SellerDashboard;

/// <summary>
/// Reads what the signed-in Seller's Dashboard shows. Every figure covers that Seller's own records only.
/// </summary>
public interface ISellerDashboardService
{
    /// <summary>
    /// Gets the Seller's stage, the period's figures, the pipeline and the batch jobs still running.
    /// </summary>
    /// <param name="days">The period: 7, 30 or 90 days; any other value reads as 30.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<DashboardSummaryResponseDto>> GetSummaryAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the work that waits on the Seller, grouped by kind and ranked by <see cref="Common.ActionItemPriority"/>.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<DashboardActionItemsResponseDto>> GetActionItemsAsync(CancellationToken cancellationToken = default);
}
