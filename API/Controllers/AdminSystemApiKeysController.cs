using APCS.Api.Extensions;
using APCS.Application.Features.Admin.SystemApiKeys;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Request;
using APCS.Application.Features.Admin.SystemApiKeys.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides Admin Portal endpoints for the platform's own AI provider keys (Ideogram, MiniMax): the keys the platform
/// makes images and videos with for plans that include them. Every endpoint answers with the whole page; no endpoint
/// ever returns a key.
/// </summary>
[ApiController]
[Route("api/admin/system-api-keys")]
[Authorize(Roles = AuthConstants.AdminRole)]
public sealed class AdminSystemApiKeysController(ISystemApiKeyAdminService service) : ControllerBase
{
    /// <summary>Gets every provider with its settings and keys, and how far each key is used today.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Adds a key, once its provider accepts it.</summary>
    [HttpPost("keys")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddKey([FromBody] AddSystemApiKeyRequestDto request, CancellationToken cancellationToken) =>
        (await service.AddKeyAsync(request, cancellationToken)).ToActionResult(this);

    /// <summary>Renames a key, or switches it on or off.</summary>
    [HttpPut("keys/{id:guid}")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateKey(Guid id, [FromBody] UpdateSystemApiKeyRequestDto request, CancellationToken cancellationToken) =>
        (await service.UpdateKeyAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Deletes a key.</summary>
    [HttpDelete("keys/{id:guid}")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteKey(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteKeyAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Checks a key with its provider again.</summary>
    [HttpPost("keys/{id:guid}/check")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckKey(Guid id, CancellationToken cancellationToken) =>
        (await service.CheckKeyAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Puts a key the provider refused today back in use.</summary>
    [HttpPost("keys/{id:guid}/clear-refused")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearRefused(Guid id, CancellationToken cancellationToken) =>
        (await service.ClearRefusedAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Changes how far each of a provider's keys is used.</summary>
    [HttpPut("providers/{provider}")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProvider(string provider, [FromBody] UpdateSystemApiKeyProviderRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.UpdateProviderAsync(provider, request, cancellationToken)).ToActionResult(this);

    /// <summary>Changes the order fill takes a provider's keys in.</summary>
    [HttpPut("providers/{provider}/order")]
    [ProducesResponseType(typeof(AdminSystemApiKeysDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reorder(string provider, [FromBody] ReorderSystemApiKeysRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.ReorderAsync(provider, request, cancellationToken)).ToActionResult(this);
}
