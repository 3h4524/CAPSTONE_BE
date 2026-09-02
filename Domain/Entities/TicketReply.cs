using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a reply posted to a support ticket.
/// </summary>
public sealed class TicketReply : AuditableEntity
{
    private TicketReply()
    {
    }

    /// <summary>
    /// Gets the parent support ticket identifier.
    /// </summary>
    public int SupportTicketId { get; private set; }

    /// <summary>
    /// Gets the seller identifier that authored the reply.
    /// </summary>
    public int AuthorId { get; private set; }

    /// <summary>
    /// Gets the author's role at the time of reply.
    /// </summary>
    public string AuthorRole { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the reply text.
    /// </summary>
    public string ReplyText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets attachment URLs.
    /// </summary>
    public string[]? AttachmentUrls { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the reply is an internal note.
    /// </summary>
    public bool IsInternalNote { get; private set; }

    /// <summary>
    /// Gets the parent support ticket.
    /// </summary>
    public SupportTicket SupportTicket { get; private set; } = null!;
}
