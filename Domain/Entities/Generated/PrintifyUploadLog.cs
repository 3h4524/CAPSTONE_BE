using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class PrintifyUploadLog
{
    public Guid Id { get; set; }

    public Guid PrintifyIntegrationId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? BatchJobId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string? PrintifyProductId { get; set; }

    public string SyncType { get; set; } = null!;

    public string UploadPayload { get; set; } = null!;

    public string? ApiResponse { get; set; }

    public string UploadStatus { get; set; } = null!;

    public string? ErrorMessage { get; set; }

    public int? RetryCount { get; set; }

    public DateTime AttemptedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual PrintifyIntegration PrintifyIntegration { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
