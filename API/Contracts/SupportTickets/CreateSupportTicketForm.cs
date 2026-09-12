using Microsoft.AspNetCore.Http;

namespace APCS.Api.Contracts.SupportTickets;

/// <summary>Multipart form used to create a support ticket.</summary>
public sealed class CreateSupportTicketForm
{
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<IFormFile> Attachments { get; set; } = [];
}
