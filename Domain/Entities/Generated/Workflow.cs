using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class Workflow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string Definition { get; set; } = null!;

    public int SchemaVersion { get; set; }

    public long Revision { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual ICollection<WorkflowRun> WorkflowRuns { get; set; } = new List<WorkflowRun>();
}
