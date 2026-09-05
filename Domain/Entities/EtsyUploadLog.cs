using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records one attempt to push a listing to Etsy.
/// </summary>
/// <remarks>
/// Publishing straight to a live listing requires explicit seller confirmation, recorded in
/// <see cref="ConfirmedByUserAtUtc"/> and enforced by a database check constraint.
/// </remarks>
public sealed class EtsyUploadLog : CreationTrackedEntity
{
    private EtsyUploadLog()
    {
    }

    /// <summary>
    /// Gets the integration used for the upload.
    /// </summary>
    public Guid EtsyIntegrationId { get; private set; }

    /// <summary>
    /// Gets the uploaded product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the key that makes this upload safe to retry.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the resulting Etsy listing identifier.
    /// </summary>
    public string? EtsyListingId { get; private set; }

    /// <summary>
    /// Gets the upload status.
    /// </summary>
    public string UploadStatus { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the resulting listing state on Etsy.
    /// </summary>
    public string ListingState { get; private set; } = "draft";

    /// <summary>
    /// Gets a value indicating whether the listing was published immediately.
    /// </summary>
    public bool PublishImmediately { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the seller confirmed immediate publication.
    /// </summary>
    public DateTimeOffset? ConfirmedByUserAtUtc { get; private set; }

    /// <summary>
    /// Gets the request payload as JSON.
    /// </summary>
    public string UploadPayload { get; private set; } = "{}";

    /// <summary>
    /// Gets the provider response as JSON.
    /// </summary>
    public string? ApiResponse { get; private set; }

    /// <summary>
    /// Gets the error message, when the upload failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the upload was attempted.
    /// </summary>
    public DateTimeOffset AttemptedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the upload finished.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the integration used for the upload.
    /// </summary>
    public EtsyIntegration EtsyIntegration { get; private set; } = null!;

    /// <summary>
    /// Gets the uploaded product.
    /// </summary>
    public Product Product { get; private set; } = null!;
}
