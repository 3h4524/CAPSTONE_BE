using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class BatchJobLog
{
    public Guid Id { get; set; }

    public Guid BatchJobId { get; set; }

    public Guid? BatchJobProductId { get; set; }

    public string LogLevel { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? Details { get; set; }

    public int? DurationMs { get; set; }

    public Guid? ApiUsageRecordId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ApiUsageRecord? ApiUsageRecord { get; set; }

    public virtual BatchJob BatchJob { get; set; } = null!;

    public virtual BatchJobProduct? BatchJobProduct { get; set; }
}
