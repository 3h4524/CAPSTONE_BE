using APCS.Api.Extensions;
using APCS.Application.Features.DesignTemplates;
using APCS.Application.Features.DesignTemplates.Dtos.Request;
using APCS.Application.Features.DesignTemplates.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Provides the authenticated Seller design-template library.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/design-templates")]
public sealed class DesignTemplatesController(IDesignTemplateService designTemplateService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DesignTemplateSummaryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] ListDesignTemplatesRequestDto request,
        CancellationToken cancellationToken) =>
        (await designTemplateService.ListAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("options")]
    [ProducesResponseType(typeof(DesignTemplateOptionsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken) =>
        (await designTemplateService.GetOptionsAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DesignTemplateDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await designTemplateService.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [ProducesResponseType(typeof(DesignTemplateDetailResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        CreateDesignTemplateRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await designTemplateService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/clone")]
    [ProducesResponseType(typeof(DesignTemplateDetailResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clone(Guid id, CancellationToken cancellationToken)
    {
        var result = await designTemplateService.CloneAsync(id, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : result.ToActionResult(this);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(DesignTemplateDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateDesignTemplateRequestDto request,
        CancellationToken cancellationToken) =>
        (await designTemplateService.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await designTemplateService.DeleteAsync(id, cancellationToken)).ToActionResult(this);
}
