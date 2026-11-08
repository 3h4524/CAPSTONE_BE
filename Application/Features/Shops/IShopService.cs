using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Shops;

/// <summary>The seller's shop: connecting Etsy and reading the connected shop.</summary>
public interface IShopService
{
    /// <summary>Starts the Etsy consent round trip for the signed-in seller.</summary>
    Task<Result<StartEtsyConnectionResponseDto>> StartEtsyConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles Etsy's callback: exchanges the code, reads the seller's shop and stores the encrypted tokens.
    /// It runs without a signed-in user (Etsy redirects the browser), so the seller is identified by the
    /// one-time state created by <see cref="StartEtsyConnectionAsync"/>. It always yields a return address.
    /// </summary>
    Task<CompleteEtsyConnectionResponseDto> CompleteEtsyConnectionAsync(string? code, string? state, string? error, CancellationToken cancellationToken = default);

    /// <summary>The signed-in seller's connection and live shop details, refreshing the access token when due.</summary>
    Task<Result<GetMyShopResponseDto>> GetMyShopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Insights from the seller's Etsy shop: health checks, listings and their issues, orders, fees and reviews.
    /// Cached for a few minutes; <paramref name="refresh"/> reads Etsy again.
    /// </summary>
    Task<Result<GetShopInsightsResponseDto>> GetShopInsightsAsync(bool refresh, CancellationToken cancellationToken = default);

    /// <summary>Disconnects the signed-in seller's Etsy shop and forgets its tokens.</summary>
    Task<Result> DisconnectEtsyAsync(CancellationToken cancellationToken = default);
}
