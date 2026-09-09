using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class NotificationAlert
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? BatchJobId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public bool? IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public string? ActionUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual ICollection<NotificationDelivery> NotificationDeliveries { get; set; } = new List<NotificationDelivery>();

    public virtual User User { get; set; } = null!;
}
