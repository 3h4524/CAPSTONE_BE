using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class EtsyUploadLog
{
    public Guid Id { get; set; }

    public Guid EtsyIntegrationId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? BatchJobId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string? EtsyListingId { get; set; }

    public string UploadStatus { get; set; } = null!;

    public string ListingState { get; set; } = null!;

    public bool PublishImmediately { get; set; }

    public DateTime? ConfirmedByUserAt { get; set; }

    public string UploadPayload { get; set; } = null!;

    public string? ApiResponse { get; set; }

    public string? ErrorMessage { get; set; }

    public int? RetryCount { get; set; }

    public DateTime AttemptedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual BatchJob? BatchJob { get; set; }

    public virtual EtsyIntegration EtsyIntegration { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
