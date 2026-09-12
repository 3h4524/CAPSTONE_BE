using APCS.Api.Contracts.SupportTickets;
using APCS.Api.Extensions;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.SupportTickets;
using APCS.Application.Features.SupportTickets.Dtos.Request;
using APCS.Application.Features.SupportTickets.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>Provides administrator support-ticket operations.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.AdminRole)]
[Route("api/admin/support-tickets")]
public sealed class AdminSupportTicketsController(ISupportTicketService supportTicketService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SupportTicketSummaryResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken) =>
        (await supportTicketService.ListAdminAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupportTicketDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await supportTicketService.GetAdminAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(SupportTicketDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateSupportTicketRequestDto request,
        CancellationToken cancellationToken) =>
        (await supportTicketService.UpdateAdminAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/replies")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportTicketReplyResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Reply(
        Guid id,
        [FromForm] CreateAdminTicketReplyForm form,
        CancellationToken cancellationToken)
    {
        var uploads = form.Attachments.Select(file => new UploadFileDto(
            Path.GetFileName(file.FileName.Replace('\\', '/')),
            file.ContentType,
            file.Length,
            file.OpenReadStream())).ToArray();
        try
        {
            var result = await supportTicketService.ReplyAdminAsync(
                id,
                new CreateAdminTicketReplyRequestDto(form.ReplyText, form.IsInternalNote, uploads),
                cancellationToken);
            return result.IsSuccess
                ? CreatedAtAction(nameof(Get), new { id }, result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            foreach (var upload in uploads)
            {
                upload.Content.Dispose();
            }
        }
    }
}
