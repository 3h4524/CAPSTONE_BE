using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class ModerationCheck
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string Feature { get; set; } = null!;

    public string Direction { get; set; } = null!;

    public string SubjectType { get; set; } = null!;

    public Guid? SubjectId { get; set; }

    public string ContentHash { get; set; } = null!;

    public string Decision { get; set; } = null!;

    public string DecidedBy { get; set; } = null!;

    public string Categories { get; set; } = null!;

    public string? Reason { get; set; }

    public string? ContentExcerpt { get; set; }

    public string? Model { get; set; }

    public int? LatencyMs { get; set; }

    public int PolicyVersion { get; set; }

    public DateTime CreatedAt { get; set; }
}
