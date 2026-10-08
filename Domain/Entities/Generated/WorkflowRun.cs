using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class WorkflowRun
{
    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    public long WorkflowRevision { get; set; }

    public string DefinitionSnapshot { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long Revision { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public Guid? ParentRunId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual ICollection<ExportPackage> ExportPackages { get; set; } = new List<ExportPackage>();

    public virtual ICollection<WorkflowRun> InverseParentRun { get; set; } = new List<WorkflowRun>();

    public virtual ICollection<MediaJob> MediaJobs { get; set; } = new List<MediaJob>();

    public virtual WorkflowRun? ParentRun { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<PromoVideo> PromoVideos { get; set; } = new List<PromoVideo>();

    public virtual User User { get; set; } = null!;

    public virtual Workflow Workflow { get; set; } = null!;

    public virtual ICollection<WorkflowNodeRun> WorkflowNodeRuns { get; set; } = new List<WorkflowNodeRun>();
}
