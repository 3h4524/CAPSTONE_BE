namespace APCS.Application.Features.SupportTickets.Dtos.Response;

/// <summary>Represents a complete support ticket and its conversation.</summary>
public sealed record SupportTicketDetailResponseDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Description,
    string Category,
    string Priority,
    string Status,
    int? SatisfactionRating,
    Guid RequesterId,
    string RequesterName,
    Guid? AssignedTo,
    string? AssignedToName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    IReadOnlyList<SupportTicketAttachmentResponseDto> Attachments,
    IReadOnlyList<SupportTicketReplyResponseDto> Replies);
