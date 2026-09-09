using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class TicketReply
{
    public Guid Id { get; set; }

    public Guid SupportTicketId { get; set; }

    public Guid AuthorId { get; set; }

    public string ReplyText { get; set; } = null!;

    public bool? IsInternalNote { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User Author { get; set; } = null!;

    public virtual SupportTicket SupportTicket { get; set; } = null!;

    public virtual ICollection<TicketAttachment> TicketAttachments { get; set; } = new List<TicketAttachment>();
}
