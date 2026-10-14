namespace APCS.Application.Abstractions.AI;

public sealed class VeoProviderException(int statusCode, string? detail = null) : Exception($"Veo request failed with status {statusCode}.")
{
    public int StatusCode { get; } = statusCode;

    /// <summary>What Google said about the refusal, when it said anything.</summary>
    public string? Detail { get; } = detail;
}

/// <summary>Only the backend handles the Seller's Gemini credential and Google video URLs.</summary>
public interface IVeoVideoProvider
{
    Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, CancellationToken ct);
    Task<VeoOperationResult> PollAsync(string apiKey, string operationName, CancellationToken ct);
    Task<byte[]> DownloadAsync(string apiKey, string videoUri, CancellationToken ct);
}

/// <param name="Done">Whether Google has finished the operation.</param>
/// <param name="VideoUri">Where the clip can be downloaded, when one was made.</param>
/// <param name="Failure">Why a finished operation has no clip, in Google's words where it gave any.</param>
/// <param name="NotCharged">Google stated that the attempt without a clip was not billed.</param>
public sealed record VeoOperationResult(bool Done, string? VideoUri, string? Failure, bool NotCharged = false);
