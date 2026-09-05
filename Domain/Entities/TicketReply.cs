using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a reply on a support ticket.
/// </summary>
public sealed class TicketReply : AuditableEntity
{
    private readonly List<TicketAttachment> _attachments = [];

    private TicketReply()
    {
    }

    /// <summary>
    /// Gets the owning ticket identifier.
    /// </summary>
    public Guid SupportTicketId { get; private set; }

    /// <summary>
    /// Gets the author's user identifier.
    /// </summary>
    public Guid AuthorId { get; private set; }

    /// <summary>
    /// Gets the reply body.
    /// </summary>
    public string ReplyText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the reply is a staff-only note.
    /// </summary>
    public bool IsInternalNote { get; private set; }

    /// <summary>
    /// Gets the owning ticket.
    /// </summary>
    public SupportTicket SupportTicket { get; private set; } = null!;

    /// <summary>
    /// Gets the files attached to this reply.
    /// </summary>
    public IReadOnlyCollection<TicketAttachment> Attachments => _attachments.AsReadOnly();
}
