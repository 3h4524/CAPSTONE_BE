using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a customer support ticket.
/// </summary>
public sealed class SupportTicket : AuditableEntity
{
    private readonly List<TicketReply> _replies = [];
    private readonly List<TicketAttachment> _attachments = [];

    private SupportTicket()
    {
    }

    /// <summary>
    /// Gets the reporting user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the human-readable ticket number.
    /// </summary>
    public string TicketNumber { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ticket subject.
    /// </summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ticket description.
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ticket category.
    /// </summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ticket priority.
    /// </summary>
    public string Priority { get; private set; } = "normal";

    /// <summary>
    /// Gets the staff user the ticket is assigned to, when assigned.
    /// </summary>
    public Guid? AssignedTo { get; private set; }

    /// <summary>
    /// Gets the ticket status.
    /// </summary>
    public string Status { get; private set; } = "open";

    /// <summary>
    /// Gets the seller's satisfaction rating, from one to five.
    /// </summary>
    public int? SatisfactionRating { get; private set; }

    /// <summary>
    /// Gets the UTC resolution timestamp.
    /// </summary>
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// Gets the replies on this ticket.
    /// </summary>
    public IReadOnlyCollection<TicketReply> Replies => _replies.AsReadOnly();

    /// <summary>
    /// Gets the files attached directly to the ticket.
    /// </summary>
    public IReadOnlyCollection<TicketAttachment> Attachments => _attachments.AsReadOnly();
}
