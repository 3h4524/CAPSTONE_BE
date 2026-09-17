using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.SupportTickets;

/// <summary>Multipart form used for a Seller follow-up.</summary>
public sealed class CreateTicketReplyForm
{
    public string ReplyText { get; set; } = string.Empty;
    public IReadOnlyList<IFormFile> Attachments { get; set; } = [];
}

/// <summary>Multipart form used for an administrator reply or internal note.</summary>
public sealed class CreateAdminTicketReplyForm
{
    public string ReplyText { get; set; } = string.Empty;
    public bool IsInternalNote { get; set; }
    public IReadOnlyList<IFormFile> Attachments { get; set; } = [];
}
