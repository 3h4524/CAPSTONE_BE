using APCS.Api.Extensions;
using APCS.Application.Features.Workflows;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Prepares ownership-checked product assets for batch export.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/exports")]
public sealed class ExportsController(IBatchExportPreviewService previews) : ControllerBase
{
    /// <summary>Lists approved assets and missing content for the selected products.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(BatchExportPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(BatchExportPreviewRequest request, CancellationToken cancellationToken) =>
        (await previews.PreviewAsync(request, cancellationToken)).ToActionResult(this);
}
