using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class WorkflowNodeRun
{
    public Guid Id { get; set; }

    public Guid WorkflowRunId { get; set; }

    public string NodeId { get; set; } = null!;

    public string NodeType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int Attempt { get; set; }

    public string InputSnapshot { get; set; } = null!;

    public string OutputSnapshot { get; set; } = null!;

    public string? Stage { get; set; }

    public int Progress { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual MediaJob? MediaJob { get; set; }

    public virtual WorkflowRun WorkflowRun { get; set; } = null!;
}
