using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ModerationAllowlist
{
    public Guid Id { get; set; }

    public string Phrase { get; set; } = null!;

    public bool IsActive { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
