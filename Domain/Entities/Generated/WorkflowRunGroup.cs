using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class WorkflowRunGroup
{
    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public Guid UserId { get; set; }

    public Guid BatchId { get; set; }

    public Guid? BatchJobId { get; set; }

    public long WorkflowRevision { get; set; }

    public string DefinitionSnapshot { get; set; } = null!;

    public string TargetOverrides { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long Revision { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Workflow Workflow { get; set; } = null!;

    public virtual ICollection<WorkflowRun> WorkflowRuns { get; set; } = new List<WorkflowRun>();
}
