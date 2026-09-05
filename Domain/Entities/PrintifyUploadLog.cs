using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records one attempt to push a product to Printify.
/// </summary>
/// <remarks>
/// <see cref="IdempotencyKey"/> is unique per integration, so a retried upload cannot create
/// a duplicate product on the marketplace.
/// </remarks>
public sealed class PrintifyUploadLog : CreationTrackedEntity
{
    private PrintifyUploadLog()
    {
    }

    /// <summary>
    /// Gets the integration used for the upload.
    /// </summary>
    public Guid PrintifyIntegrationId { get; private set; }

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
    /// Gets the resulting Printify product identifier.
    /// </summary>
    public string? PrintifyProductId { get; private set; }

    /// <summary>
    /// Gets whether the upload created or updated the marketplace product.
    /// </summary>
    public string SyncType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the request payload as JSON.
    /// </summary>
    public string UploadPayload { get; private set; } = "{}";

    /// <summary>
    /// Gets the provider response as JSON.
    /// </summary>
    public string? ApiResponse { get; private set; }

    /// <summary>
    /// Gets the upload status.
    /// </summary>
    public string UploadStatus { get; private set; } = string.Empty;

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
    public PrintifyIntegration PrintifyIntegration { get; private set; } = null!;

    /// <summary>
    /// Gets the uploaded product.
    /// </summary>
    public Product Product { get; private set; } = null!;
}
