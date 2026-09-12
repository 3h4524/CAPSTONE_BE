using APCS.Application.Abstractions.Storage;

namespace APCS.Application.Features.SupportTickets.Dtos.Request;

/// <summary>Requests a public Seller follow-up on a support ticket.</summary>
public sealed record CreateTicketReplyRequestDto(
    string ReplyText,
    IReadOnlyCollection<UploadFileDto> Attachments);

/// <summary>Requests an administrator reply or internal note.</summary>
public sealed record CreateAdminTicketReplyRequestDto(
    string ReplyText,
    bool IsInternalNote,
    IReadOnlyCollection<UploadFileDto> Attachments);
