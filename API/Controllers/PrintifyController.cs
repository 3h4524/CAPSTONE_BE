using APCS.Api.Extensions;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// The seller's Printify store (its key is managed on API Keys): browsing the catalog, and pushing a
/// product's approved design to Printify, which publishes it to Etsy and fulfils its orders.
/// </summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/shop/printify")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PrintifyController(IPrintifyService service) : ControllerBase
{
    /// <summary>Whether the Printify key from API Keys works; <c>refresh=true</c> asks Printify again.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PrintifyConnectionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        (await service.GetConnectionAsync(refresh, cancellationToken)).ToActionResult(this);
    [HttpGet("blueprints")]
    [ProducesResponseType(typeof(IReadOnlyList<PrintifyBlueprintDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Blueprints([FromQuery] string? q, CancellationToken cancellationToken) =>
        (await service.SearchBlueprintsAsync(q, cancellationToken)).ToActionResult(this);

    [HttpGet("blueprints/{blueprintId:long}/providers")]
    [ProducesResponseType(typeof(IReadOnlyList<PrintifyProviderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Providers(long blueprintId, CancellationToken cancellationToken) =>
        (await service.GetProvidersAsync(blueprintId, cancellationToken)).ToActionResult(this);

    [HttpGet("blueprints/{blueprintId:long}/providers/{providerId:long}")]
    [ProducesResponseType(typeof(PrintifyCatalogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(long blueprintId, long providerId, CancellationToken cancellationToken) =>
        (await service.GetCatalogAsync(blueprintId, providerId, cancellationToken)).ToActionResult(this);

    [HttpGet("drafts/{productId:guid}")]
    [ProducesResponseType(typeof(PrintifyDraftResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDraft(Guid productId, CancellationToken cancellationToken) =>
        (await service.GetDraftAsync(productId, cancellationToken)).ToActionResult(this);

    /// <summary>Creates the product on Printify only, returning its costs for pricing.</summary>
    [HttpPost("drafts/{productId:guid}")]
    [ProducesResponseType(typeof(PrintifyProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid productId, CreatePrintifyProductRequestDto request, CancellationToken cancellationToken) =>
        (await service.CreateProductAsync(productId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Sets prices and asks Printify to publish to the linked Etsy shop.</summary>
    [HttpPost("drafts/{productId:guid}/products/{printifyProductId}/publish")]
    [ProducesResponseType(typeof(PrintifyProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(Guid productId, string printifyProductId, PublishPrintifyProductRequestDto request, CancellationToken cancellationToken) =>
        (await service.PublishAsync(productId, printifyProductId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Reads publishing progress and, once on Etsy, adds the chosen APCS media to the listing.</summary>
    [HttpPost("drafts/{productId:guid}/products/{printifyProductId}/sync")]
    [ProducesResponseType(typeof(PrintifyProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync(Guid productId, string printifyProductId, SyncPrintifyProductRequestDto request, CancellationToken cancellationToken) =>
        (await service.SyncAsync(productId, printifyProductId, request, cancellationToken)).ToActionResult(this);
}
