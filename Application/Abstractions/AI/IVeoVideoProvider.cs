using APCS.Application.Features.Workflows;

namespace APCS.Application.Abstractions.AI;

public class AiVideoProviderException(int statusCode, string? detail = null) : Exception($"AI video provider request failed with status {statusCode}.")
{
    public int StatusCode { get; } = statusCode;

    /// <summary>Provider-specific details, when returned.</summary>
    public string? Detail { get; } = detail;
}

public sealed class VeoProviderException(int statusCode, string? detail = null) : AiVideoProviderException(statusCode, detail);

/// <summary>Only the backend handles the Seller's Gemini credential and Google video URLs.</summary>
public interface IVeoVideoProvider
{
    Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, CancellationToken ct);
    Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, Region? sourceCrop, CancellationToken ct) =>
        StartAsync(apiKey, sourceImageUrl, prompt, aspectRatio, durationSeconds, people, ct);
    Task<VeoOperationResult> PollAsync(string apiKey, string operationName, CancellationToken ct);
    Task<byte[]> DownloadAsync(string apiKey, string videoUri, CancellationToken ct);
}

/// <summary>A provider strategy that owns one provider/model combination.</summary>
public interface IAiVideoGenerationStrategy : IVeoVideoProvider
{
    string ProviderKey { get; }
    string ModelKey { get; }
    string ModelName { get; }
}

/// <summary>Resolves the provider strategy captured in a scene's immutable generation plan.</summary>
public interface IAiVideoGenerationStrategyFactory
{
    IAiVideoGenerationStrategy Resolve(string providerKey, string modelKey);
}

/// <param name="Done">Whether Google has finished the operation.</param>
/// <param name="VideoUri">Where the clip can be downloaded, when one was made.</param>
/// <param name="Failure">Why a finished operation has no clip, in Google's words where it gave any.</param>
/// <param name="NotCharged">Google stated that the attempt without a clip was not billed.</param>
public sealed record VeoOperationResult(bool Done, string? VideoUri, string? Failure, bool NotCharged = false);
