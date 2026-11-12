using APCS.Api.Extensions;
using APCS.Application.Features.Admin.MockupTemplates;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for linking system mock-up templates to the Printify product their photo shows.
/// </summary>
[ApiController]
[Route("api/admin/mockup-templates")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminMockupTemplatesController(IAdminMockupTemplateService service) : ControllerBase
{
    /// <summary>Gets every system mock-up template with its Printify link.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminMockupTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Links a system template to the Printify product its photo shows, or changes the link.</summary>
    [HttpPut("{id:guid}/printify")]
    [ProducesResponseType(typeof(AdminMockupTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkPrintify(Guid id, [FromBody] LinkMockupTemplatePrintifyRequestDto request, CancellationToken cancellationToken) =>
        (await service.LinkPrintifyAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Removes a system template's Printify link.</summary>
    [HttpDelete("{id:guid}/printify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkPrintify(Guid id, CancellationToken cancellationToken) =>
        (await service.UnlinkPrintifyAsync(id, cancellationToken)).ToActionResult(this);
}
