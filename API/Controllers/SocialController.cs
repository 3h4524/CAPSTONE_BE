using APCS.Api.Extensions;
using APCS.Application.Features.Social;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Promoting a product on social media: posts prepared from its approved media and listing.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/social")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SocialController(ISocialShareKitService kits) : ControllerBase
{
    /// <summary>The product's Pinterest and TikTok posts: sized files to download and text to copy.</summary>
    [HttpGet("kits/{productId:guid}")]
    [ProducesResponseType(typeof(SocialShareKitDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKit(Guid productId, CancellationToken cancellationToken) =>
        (await kits.GetAsync(productId, cancellationToken)).ToActionResult(this);
}
