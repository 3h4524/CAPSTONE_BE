using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PinterestIntegration
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PinterestUsername { get; set; } = null!;

    public string AccessTokenEncrypted { get; set; } = null!;

    public string? RefreshTokenEncrypted { get; set; }

    public DateTime? AccessExpiresAt { get; set; }

    public DateTime? RefreshExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
