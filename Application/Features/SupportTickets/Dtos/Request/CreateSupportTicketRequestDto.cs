using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.SupportTickets.Dtos.Request;

/// <summary>Requests creation of a Seller support ticket.</summary>
public sealed record CreateSupportTicketRequestDto(
    string Subject,
    string Category,
    string Priority,
    string Description,
    IReadOnlyCollection<UploadFileDto> Attachments);
