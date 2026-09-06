using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ApiKey
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ServiceProvider { get; set; } = null!;

    public string KeyIdentifier { get; set; } = null!;

    public string KeyValueEncrypted { get; set; } = null!;

    public string? KeyLast4 { get; set; }

    public bool? IsActive { get; set; }

    public int? UsageCount { get; set; }

    public int? UsageLimit { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? CreatedByIp { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
