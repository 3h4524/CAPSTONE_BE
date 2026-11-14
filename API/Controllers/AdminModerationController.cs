using APCS.Api.Extensions;
using APCS.Application.Features.Admin.Moderation;
using APCS.Application.Features.Admin.Moderation.Dtos;
using APCS.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Admin endpoints for the Content Safety page: what the content guardrail decided, its blocklist and allowlist, and
/// the settings an administrator can change at runtime. See docs/CONTENT_MODERATION.md.
/// </summary>
[ApiController]
[Route("api/admin/moderation")]
[Authorize(Roles = "Admin,Super Admin")]
public sealed class AdminModerationController(IAdminModerationService moderation) : ControllerBase
{
    /// <summary>Totals, a per-day series and breakdowns over the last 7, 30 or 90 days.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(ModerationOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        (await moderation.GetOverviewAsync(days, cancellationToken)).ToActionResult(this);

    /// <summary>The sellers blocked most often over the period.</summary>
    [HttpGet("offenders")]
    [ProducesResponseType(typeof(IReadOnlyList<ModerationOffenderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffenders([FromQuery] int days = 30, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        (await moderation.GetTopOffendersAsync(days, limit, cancellationToken)).ToActionResult(this);

    /// <summary>A filtered page of the moderation log, newest first.</summary>
    [HttpGet("checks")]
    [ProducesResponseType(typeof(PagedResult<ModerationCheckListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListChecks([FromQuery] ListModerationChecksRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.ListChecksAsync(request, cancellationToken)).ToActionResult(this);

    /// <summary>One check with its findings and, when it was not allowed, the seller's text.</summary>
    [HttpGet("checks/{id:guid}")]
    [ProducesResponseType(typeof(ModerationCheckDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCheck(Guid id, CancellationToken cancellationToken) =>
        (await moderation.GetCheckAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("terms")]
    [ProducesResponseType(typeof(IReadOnlyList<ModerationTermDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTerms([FromQuery] ListModerationTermsRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.ListTermsAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPost("terms")]
    [ProducesResponseType(typeof(ModerationTermDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTerm(SaveModerationTermRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.CreateTermAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPut("terms/{id:guid}")]
    [ProducesResponseType(typeof(ModerationTermDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTerm(Guid id, SaveModerationTermRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.UpdateTermAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("terms/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTerm(Guid id, CancellationToken cancellationToken) =>
        (await moderation.DeleteTermAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("allowlist")]
    [ProducesResponseType(typeof(IReadOnlyList<ModerationAllowlistDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAllowlist(CancellationToken cancellationToken) =>
        (await moderation.ListAllowlistAsync(cancellationToken)).ToActionResult(this);

    [HttpPost("allowlist")]
    [ProducesResponseType(typeof(ModerationAllowlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAllowlist(SaveModerationAllowlistRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.CreateAllowlistAsync(request, cancellationToken)).ToActionResult(this);

    [HttpPut("allowlist/{id:guid}")]
    [ProducesResponseType(typeof(ModerationAllowlistDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAllowlist(Guid id, SaveModerationAllowlistRequestDto request,
        CancellationToken cancellationToken) =>
        (await moderation.UpdateAllowlistAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("allowlist/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAllowlist(Guid id, CancellationToken cancellationToken) =>
        (await moderation.DeleteAllowlistAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("settings")]
    [ProducesResponseType(typeof(ModerationSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        (await moderation.GetSettingsAsync(cancellationToken)).ToActionResult(this);

    [HttpPut("settings")]
    [ProducesResponseType(typeof(ModerationSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSettings(UpdateModerationSettingsRequestDto request, CancellationToken cancellationToken) =>
        (await moderation.UpdateSettingsAsync(request, cancellationToken)).ToActionResult(this);
}
