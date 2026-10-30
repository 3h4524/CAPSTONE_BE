using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Rag.Dtos;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

[ApiController]
[Authorize(Roles = $"{AuthConstants.UserRole},{AuthConstants.AdminRole}")]
[Route("api/rag")]
public sealed class RagController(IRagService service) : ControllerBase
{
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest(RagIngestRequest request, CancellationToken cancellationToken)
    {
        var documentId = await service.IngestAsync(request, cancellationToken);
        return Accepted(new { documentId, status = "queued" });
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(RagSearchRequest request, CancellationToken cancellationToken)
    {
        var results = await service.SearchAsync(request, cancellationToken);
        return Ok(results);
    }

    [HttpPost("admin/search")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> SearchWithDiagnostics(
        RagSearchRequest request,
        CancellationToken cancellationToken)
        => Ok(await service.SearchWithDiagnosticsAsync(request, cancellationToken));

    [HttpPost("admin/evaluation")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> Evaluate(
        RagEvaluationRequest request,
        CancellationToken cancellationToken)
        => Ok(await service.EvaluateAsync(request, cancellationToken));

    [HttpGet("admin/health")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> GetCorpusHealth(
        [FromQuery] string? contentType,
        [FromQuery] int staleDays = 30,
        CancellationToken cancellationToken = default)
        => Ok(await service.GetCorpusHealthAsync(contentType, staleDays, cancellationToken));

    [HttpPost("admin/reingest")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> Reingest(
        RagReingestRequest request,
        CancellationToken cancellationToken)
        => Ok(await service.ReingestAsync(request, cancellationToken));

    [HttpGet("admin/documents")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> GetAdminDocuments(
        [FromQuery] string? status,
        [FromQuery] string? sourceId,
        CancellationToken cancellationToken) =>
        Ok(await service.GetAdminDocumentsAsync(status, sourceId, cancellationToken));

    [HttpGet("admin/documents/{documentId:guid}")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> GetAdminDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await service.GetAdminDocumentAsync(documentId, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("admin/documents/{documentId:guid}/activate")]
    [Authorize(Roles = AuthConstants.AdminRole)]
    public async Task<IActionResult> ActivateDocument(Guid documentId, CancellationToken cancellationToken)
    {
        var activated = await service.ActivateDocumentAsync(documentId, cancellationToken);
        return activated ? NoContent() : BadRequest(new { message = "Only a completed RAG document can be activated." });
    }
}
