namespace APCS.Application.Features.SupportTickets.Dtos.Response;

/// <summary>Represents one message in a support-ticket conversation.</summary>
public sealed record SupportTicketReplyResponseDto(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string AuthorRole,
    string ReplyText,
    bool IsInternalNote,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SupportTicketAttachmentResponseDto> Attachments);
