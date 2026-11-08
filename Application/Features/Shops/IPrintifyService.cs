using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Shops;

/// <summary>
/// Sells a product's approved design as a print-on-demand item through the seller's Printify store: APCS creates
/// the product on Printify, Printify publishes it to Etsy and fulfils its orders, and APCS then adds its own
/// mock-ups and video to that Etsy listing.
/// </summary>
public interface IPrintifyService
{
    /// <summary>
    /// Whether the Printify key saved on API Keys works and which store it publishes through. The store is remembered
    /// a few minutes; <paramref name="refresh"/> asks Printify again.
    /// </summary>
    Task<Result<PrintifyConnectionDto>> GetConnectionAsync(bool refresh, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PrintifyBlueprintDto>>> SearchBlueprintsAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>The providers that make a blueprint, those shipping from the United States first.</summary>
    Task<Result<IReadOnlyList<PrintifyProviderDto>>> GetProvidersAsync(long blueprintId, CancellationToken cancellationToken = default);

    Task<Result<PrintifyCatalogDto>> GetCatalogAsync(long blueprintId, long providerId, CancellationToken cancellationToken = default);

    Task<Result<PrintifyDraftResponseDto>> GetDraftAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Creates the product on Printify only; its costs come back so the seller can set prices before publishing.</summary>
    Task<Result<PrintifyProductDto>> CreateProductAsync(Guid productId, CreatePrintifyProductRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Sets the confirmed prices and asks Printify to publish the product to the linked Etsy shop.</summary>
    Task<Result<PrintifyProductDto>> PublishAsync(Guid productId, string printifyProductId, PublishPrintifyProductRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads how far publishing got. Once Printify's Etsy listing exists, adds the chosen APCS mock-ups and video
    /// to it, once.
    /// </summary>
    Task<Result<PrintifyProductDto>> SyncAsync(Guid productId, string printifyProductId, SyncPrintifyProductRequestDto request, CancellationToken cancellationToken = default);
}
