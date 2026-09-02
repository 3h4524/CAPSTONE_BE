using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a support request opened by a seller.
/// </summary>
public sealed class SupportTicket : AuditableEntity
{
    private readonly List<TicketReply> _replies = [];

    private SupportTicket()
    {
    }

    /// <summary>
    /// Gets the seller identifier that opened the ticket.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the public ticket number.
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
    /// Gets the support category.
    /// </summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the ticket priority.
    /// </summary>
    public string Priority { get; private set; } = "normal";

    /// <summary>
    /// Gets the assigned administrator identifier, when assigned.
    /// </summary>
    public int? AssignedToAdminId { get; private set; }

    /// <summary>
    /// Gets the ticket status.
    /// </summary>
    public string Status { get; private set; } = "open";

    /// <summary>
    /// Gets attachment URLs.
    /// </summary>
    public string[]? AttachmentUrls { get; private set; }

    /// <summary>
    /// Gets the seller satisfaction rating.
    /// </summary>
    public int? SatisfactionRating { get; private set; }

    /// <summary>
    /// Gets the UTC ticket resolution timestamp.
    /// </summary>
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// Gets the assigned administrator, when assigned.
    /// </summary>
    public Admin? AssignedToAdmin { get; private set; }

    /// <summary>
    /// Gets the ticket replies.
    /// </summary>
    public IReadOnlyCollection<TicketReply> Replies => _replies.AsReadOnly();
}
