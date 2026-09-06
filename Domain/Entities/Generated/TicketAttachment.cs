using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class TicketAttachment
{
    public Guid Id { get; set; }

    public Guid? SupportTicketId { get; set; }

    public Guid? TicketReplyId { get; set; }

    public string FileUrl { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string MimeType { get; set; } = null!;

    public decimal FileSizeMb { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual SupportTicket? SupportTicket { get; set; }

    public virtual TicketReply? TicketReply { get; set; }

    public virtual User UploadedByNavigation { get; set; } = null!;
}
