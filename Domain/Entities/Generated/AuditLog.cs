using System;
using System.Collections.Generic;
using System.Net;

namespace APCS.Domain.Entities;

public partial class AuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public string ActionType { get; set; } = null!;

    public string ResourceType { get; set; } = null!;

    public Guid? ResourceId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? ActorUser { get; set; }
}
