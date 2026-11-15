using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ModerationTerm
{
    public Guid Id { get; set; }

    public string Term { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string MatchType { get; set; } = null!;

    public bool MatchUnaccented { get; set; }

    public string Language { get; set; } = null!;

    public bool IsActive { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
