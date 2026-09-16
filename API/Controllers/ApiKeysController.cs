using APCS.Api.Extensions;
using APCS.Application.Features.ApiKeys;
using APCS.Application.Features.ApiKeys.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/api-keys")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ApiKeysController(IApiKeyService service) : ControllerBase
{
    [HttpGet("providers")]
    public async Task<IActionResult> Providers(CancellationToken cancellationToken) =>
        (await service.ListProvidersAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    public async Task<IActionResult> Add(APCS.Application.Features.ApiKeys.Dtos.Request.SaveApiKeyRequestDto request, CancellationToken cancellationToken) =>
        (await service.SaveAsync(null, request, cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, APCS.Application.Features.ApiKeys.Dtos.Request.SaveApiKeyRequestDto request, CancellationToken cancellationToken) =>
        (await service.SaveAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken) =>
        (await service.ValidateAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet]
    [ProducesResponseType(typeof(ListApiKeysResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListMineAsync(cancellationToken)).ToActionResult(this);
}
