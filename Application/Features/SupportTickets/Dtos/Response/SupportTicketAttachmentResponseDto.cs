namespace APCS.Application.Features.SupportTickets.Dtos.Response;

/// <summary>Represents a private ticket attachment with a temporary download link.</summary>
public sealed record SupportTicketAttachmentResponseDto(
    Guid Id,
    string FileName,
    string MimeType,
    decimal FileSizeMb,
    string DownloadUrl,
    DateTimeOffset DownloadUrlExpiresAtUtc);
