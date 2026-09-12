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

/// <summary>Provides the authenticated Seller support-ticket experience.</summary>
[ApiController]
[Authorize(Roles = AuthConstants.UserRole)]
[Route("api/support-tickets")]
public sealed class SupportTicketsController(ISupportTicketService supportTicketService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportTicketSummaryResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromForm] CreateSupportTicketForm form,
        CancellationToken cancellationToken)
    {
        var uploads = OpenUploads(form.Attachments);
        try
        {
            var result = await supportTicketService.CreateAsync(
                new CreateSupportTicketRequestDto(
                    form.Subject,
                    form.Category,
                    form.Priority,
                    form.Description,
                    uploads),
                cancellationToken);

            return result.IsSuccess
                ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            DisposeUploads(uploads);
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SupportTicketSummaryResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] ListSupportTicketsRequestDto request,
        CancellationToken cancellationToken) =>
        (await supportTicketService.ListMineAsync(request, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupportTicketDetailResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        (await supportTicketService.GetMineAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/replies")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportTicketReplyResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reply(
        Guid id,
        [FromForm] CreateTicketReplyForm form,
        CancellationToken cancellationToken)
    {
        var uploads = OpenUploads(form.Attachments);
        try
        {
            var result = await supportTicketService.ReplyAsync(
                id,
                new CreateTicketReplyRequestDto(form.ReplyText, uploads),
                cancellationToken);
            return result.IsSuccess
                ? CreatedAtAction(nameof(Get), new { id }, result.Value)
                : result.ToActionResult(this);
        }
        finally
        {
            DisposeUploads(uploads);
        }
    }

    [HttpPut("{id:guid}/satisfaction-rating")]
    [ProducesResponseType(typeof(SupportTicketSummaryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rate(
        Guid id,
        RateSupportTicketRequestDto request,
        CancellationToken cancellationToken) =>
        (await supportTicketService.RateAsync(id, request, cancellationToken)).ToActionResult(this);

    private static IReadOnlyList<UploadFileDto> OpenUploads(IEnumerable<IFormFile> files) =>
        files.Select(file => new UploadFileDto(
            SanitizeFileName(file.FileName),
            file.ContentType,
            file.Length,
            file.OpenReadStream())).ToArray();

    private static void DisposeUploads(IEnumerable<UploadFileDto> uploads)
    {
        foreach (var upload in uploads)
        {
            upload.Content.Dispose();
        }
    }

    private static string SanitizeFileName(string fileName) =>
        Path.GetFileName(fileName.Replace('\\', '/'));
}
