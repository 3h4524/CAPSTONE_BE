using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class MediaJob
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public Guid WorkflowNodeRunId { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public int Attempt { get; set; }

    public int MaximumAttempts { get; set; }

    public Guid? LeaseToken { get; set; }

    public DateTime? LeaseExpiresAt { get; set; }

    public DateTime? HeartbeatAt { get; set; }

    public DateTime AvailableAt { get; set; }

    public int Progress { get; set; }

    public string? Stage { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual WorkflowNodeRun WorkflowNodeRun { get; set; } = null!;

    public virtual WorkflowRun WorkflowRun { get; set; } = null!;
}
