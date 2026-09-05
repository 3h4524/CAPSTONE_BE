using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a file attached to a support ticket or to one of its replies.
/// </summary>
/// <remarks>
/// Exactly one owner is set: either the ticket or the reply, never both and never neither.
/// </remarks>
public sealed class TicketAttachment : CreationTrackedEntity
{
    private TicketAttachment()
    {
    }

    /// <summary>
    /// Gets the owning ticket identifier, when attached to the ticket itself.
    /// </summary>
    public Guid? SupportTicketId { get; private set; }

    /// <summary>
    /// Gets the owning reply identifier, when attached to a reply.
    /// </summary>
    public Guid? TicketReplyId { get; private set; }

    /// <summary>
    /// Gets the file URL.
    /// </summary>
    public string FileUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the original file name.
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the file MIME type.
    /// </summary>
    public string MimeType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the file size in megabytes.
    /// </summary>
    public decimal FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the uploading user identifier.
    /// </summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>
    /// Gets the owning ticket, when attached to the ticket itself.
    /// </summary>
    public SupportTicket? SupportTicket { get; private set; }

    /// <summary>
    /// Gets the owning reply, when attached to a reply.
    /// </summary>
    public TicketReply? TicketReply { get; private set; }
}
