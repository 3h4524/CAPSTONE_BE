using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Records a single billable call to an external AI or marketplace provider.
/// </summary>
/// <remarks>
/// Central ledger for cost and latency. Generated assets link back here rather than each
/// carrying their own copy of the cost and duration figures.
/// </remarks>
public sealed class ApiUsageRecord : CreationTrackedEntity
{
    private ApiUsageRecord()
    {
    }

    /// <summary>
    /// Gets the billed user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the originating batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the related product identifier, when applicable.
    /// </summary>
    public Guid? ProductId { get; private set; }

    /// <summary>
    /// Gets the provider that served the call.
    /// </summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the feature the call was made for.
    /// </summary>
    public string Feature { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the provider model name.
    /// </summary>
    public string? ModelName { get; private set; }

    /// <summary>
    /// Gets the provider-side request identifier, used for reconciliation.
    /// </summary>
    public string? ProviderRequestId { get; private set; }

    /// <summary>
    /// Gets the number of billable units consumed.
    /// </summary>
    public int RequestUnits { get; private set; } = 1;

    /// <summary>
    /// Gets the number of input tokens, for token-billed providers.
    /// </summary>
    public int? TokensInput { get; private set; }

    /// <summary>
    /// Gets the number of output tokens, for token-billed providers.
    /// </summary>
    public int? TokensOutput { get; private set; }

    /// <summary>
    /// Gets the call cost in USD.
    /// </summary>
    public decimal CostUsd { get; private set; }

    /// <summary>
    /// Gets the call latency in milliseconds.
    /// </summary>
    public int? LatencyMs { get; private set; }

    /// <summary>
    /// Gets the call outcome.
    /// </summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the provider error code, when the call failed.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    /// Gets the originating batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the related product, when applicable.
    /// </summary>
    public Product? Product { get; private set; }
}
