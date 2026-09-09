using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SupportTicket
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TicketNumber { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public Guid? AssignedTo { get; set; }

    public string Status { get; set; } = null!;

    public int? SatisfactionRating { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual ICollection<TicketAttachment> TicketAttachments { get; set; } = new List<TicketAttachment>();

    public virtual ICollection<TicketReply> TicketReplies { get; set; } = new List<TicketReply>();

    public virtual User User { get; set; } = null!;
}
