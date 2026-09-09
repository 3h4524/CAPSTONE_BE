using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class AuthToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenType { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
