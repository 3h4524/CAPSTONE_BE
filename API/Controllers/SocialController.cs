using APCS.Api.Extensions;
using APCS.Application.Features.Social;
using APCS.Application.Features.Social.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Promoting a product on social media: posts prepared from its approved media and listing, and Pinterest.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/social")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SocialController(ISocialShareKitService kits, IPinterestService pinterest) : ControllerBase
{
    /// <summary>The product's Pinterest and TikTok posts: sized files to download and text to copy.</summary>
    [HttpGet("kits/{productId:guid}")]
    [ProducesResponseType(typeof(SocialShareKitDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKit(Guid productId, CancellationToken cancellationToken) =>
        (await kits.GetAsync(productId, cancellationToken)).ToActionResult(this);

    /// <summary>The seller's Pinterest connection.</summary>
    [HttpGet("pinterest")]
    [ProducesResponseType(typeof(PinterestConnectionResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPinterest(CancellationToken cancellationToken) =>
        (await pinterest.GetConnectionAsync(cancellationToken)).ToActionResult(this);

    /// <summary>The Pinterest consent page to open for connecting an account.</summary>
    [HttpPost("pinterest/connect")]
    [ProducesResponseType(typeof(StartPinterestConnectionResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConnectPinterest(CancellationToken cancellationToken) =>
        (await pinterest.StartConnectionAsync(cancellationToken)).ToActionResult(this);

    /// <summary>The boards of the connected Pinterest account.</summary>
    [HttpGet("pinterest/boards")]
    [ProducesResponseType(typeof(IReadOnlyList<PinterestBoardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPinterestBoards(CancellationToken cancellationToken) =>
        (await pinterest.GetBoardsAsync(cancellationToken)).ToActionResult(this);

    [HttpDelete("pinterest")]
    public async Task<IActionResult> DisconnectPinterest(CancellationToken cancellationToken) =>
        (await pinterest.DisconnectAsync(cancellationToken)).ToActionResult(this);

    /// <summary>
    /// Pinterest redirects the seller's browser here after the consent page. It carries no APCS session, so it is
    /// anonymous: the seller is identified by the one-time state issued by <see cref="ConnectPinterest"/>. The path is
    /// the redirect URI registered on the Pinterest app and must not change without updating that setting.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/api/pinterest/oauth/callback")]
    public async Task<IActionResult> PinterestCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        var result = await pinterest.CompleteConnectionAsync(code, state, error, cancellationToken);
        return Redirect(result.RedirectUrl);
    }
}
