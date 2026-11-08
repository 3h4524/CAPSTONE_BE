using APCS.Api.Extensions;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>The seller's shop: connecting an Etsy account and reading the connected shop.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/shop")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShopController(IShopService service, IEtsyPublishingService publishing) : ControllerBase
{
    /// <summary>The push form for a product: its content, approved media, the shop's choices and remembered settings.</summary>
    [HttpGet("etsy/drafts/{productId:guid}")]
    [ProducesResponseType(typeof(EtsyDraftResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDraft(Guid productId, CancellationToken cancellationToken) =>
        (await publishing.GetDraftAsync(productId, cancellationToken)).ToActionResult(this);

    /// <summary>Creates a draft listing on Etsy from the product; nothing is published.</summary>
    [HttpPost("etsy/drafts/{productId:guid}")]
    [ProducesResponseType(typeof(PublishEtsyDraftResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishDraft(Guid productId, APCS.Application.Features.Shops.Dtos.Request.PublishEtsyDraftRequestDto request, CancellationToken cancellationToken) =>
        (await publishing.PublishDraftAsync(productId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Etsy categories matching the words of <paramref name="q"/>.</summary>
    [HttpGet("etsy/taxonomy")]
    public async Task<IActionResult> Taxonomy([FromQuery] string? q, CancellationToken cancellationToken) =>
        (await publishing.SearchTaxonomyAsync(q, cancellationToken)).ToActionResult(this);

    /// <summary>The seller's connection status and, when connected, the live shop details.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetMyShopResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetMyShopAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Insights from the connected shop; cached a few minutes, <c>refresh=true</c> reads Etsy again.</summary>
    [HttpGet("insights")]
    [ProducesResponseType(typeof(GetShopInsightsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Insights([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        (await service.GetShopInsightsAsync(refresh, cancellationToken)).ToActionResult(this);

    /// <summary>Starts connecting Etsy: returns the consent page the client sends the browser to.</summary>
    [HttpPost("etsy/connect")]
    [ProducesResponseType(typeof(StartEtsyConnectionResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Connect(CancellationToken cancellationToken) =>
        (await service.StartEtsyConnectionAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Disconnects the seller's Etsy shop and forgets its tokens.</summary>
    [HttpDelete("etsy")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken) =>
        (await service.DisconnectEtsyAsync(cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Etsy redirects the seller's browser here after the consent page. It carries no APCS session, so it is
    /// anonymous: the seller is identified by the one-time state issued by <see cref="Connect"/>. The path is
    /// the callback registered on the Etsy app and must not change without updating that setting.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/api/etsy/oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var result = await service.CompleteEtsyConnectionAsync(code, state, error, cancellationToken);
        return Redirect(result.RedirectUrl);
    }
}
