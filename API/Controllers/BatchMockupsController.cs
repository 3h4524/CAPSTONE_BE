using APCS.Api.Extensions;
using APCS.Application.Features.BatchMockups;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Route("api/batch-jobs/{batchJobId:guid}/mockups")]
[Authorize(Roles = AuthConstants.UserRole)]
public sealed class BatchMockupsController(IMockupTemplateService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(BatchMockupSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.GetSelectionAsync(batchJobId, cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(typeof(BatchMockupSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply(
        Guid batchJobId,
        [FromBody] ApplyMockupTemplatesRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.ApplyAsync(batchJobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>The mock-ups the job already has for its selected templates. Makes none.</summary>
    [HttpGet("images")]
    [ProducesResponseType(typeof(GenerateAllMockupsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImages(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.GetJobMockupsAsync(batchJobId, cancellationToken)).ToActionResult(this);

    /// <summary>Composites mock-ups for every product in the batch job at once. Safe to call again.</summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(GenerateAllMockupsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateAll(Guid batchJobId, CancellationToken cancellationToken) =>
        (await service.GenerateAllAsync(batchJobId, cancellationToken)).ToActionResult(this);

    /// <summary>Makes one of the job's mock-ups its own way, apart from the job's settings, and returns the job's mock-ups.</summary>
    [HttpPut("overrides")]
    [ProducesResponseType(typeof(GenerateAllMockupsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveOverride(
        Guid batchJobId,
        [FromBody] SaveMockupOverrideRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.SaveOverrideAsync(batchJobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Makes several of the job's mock-ups their own way at once, all or none, and returns the job's mock-ups.</summary>
    [HttpPut("overrides/bulk")]
    [ProducesResponseType(typeof(GenerateAllMockupsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveOverrides(
        Guid batchJobId,
        [FromBody] SaveMockupOverridesRequestDto request,
        CancellationToken cancellationToken) =>
        (await service.SaveOverridesAsync(batchJobId, request, cancellationToken)).ToActionResult(this);

    /// <summary>Puts one of the job's mock-ups back on the job's settings and returns the job's mock-ups.</summary>
    [HttpDelete("overrides/{slotKey}")]
    [ProducesResponseType(typeof(GenerateAllMockupsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetOverride(
        Guid batchJobId,
        string slotKey,
        [FromQuery] Guid? replacedMockupImageId,
        CancellationToken cancellationToken) =>
        (await service.ResetOverrideAsync(batchJobId, slotKey, replacedMockupImageId, cancellationToken)).ToActionResult(this);
}
