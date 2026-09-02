using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one Etsy listing upload attempt.
/// </summary>
public sealed class EtsyUploadLog : CreationTrackedEntity
{
    private EtsyUploadLog()
    {
    }

    /// <summary>
    /// Gets the Etsy integration identifier.
    /// </summary>
    public int EtsyIntegrationId { get; private set; }

    /// <summary>
    /// Gets the source product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the external Etsy listing identifier.
    /// </summary>
    public string? EtsyListingId { get; private set; }

    /// <summary>
    /// Gets the upload status.
    /// </summary>
    public string UploadStatus { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the listing should be published immediately.
    /// </summary>
    public bool PublishImmediately { get; private set; }

    /// <summary>
    /// Gets the submitted upload payload as JSON.
    /// </summary>
    public string UploadPayload { get; private set; } = "{}";

    /// <summary>
    /// Gets the external API response as JSON.
    /// </summary>
    public string? ApiResponse { get; private set; }

    /// <summary>
    /// Gets the last upload error message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the number of retry attempts.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the upload attempt.
    /// </summary>
    public DateTimeOffset AttemptedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC completion timestamp.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the Etsy integration.
    /// </summary>
    public EtsyIntegration EtsyIntegration { get; private set; } = null!;

    /// <summary>
    /// Gets the source product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }
}
