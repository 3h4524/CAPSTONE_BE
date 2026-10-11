namespace APCS.Application.Abstractions.AI;

public sealed class VeoProviderException(int statusCode) : Exception($"Veo request failed with status {statusCode}.")
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Only the backend handles the Seller's Gemini credential and Google video URLs.</summary>
public interface IVeoVideoProvider
{
    Task<string> StartAsync(string apiKey, string sourceImageUrl, string prompt, string aspectRatio,
        int durationSeconds, bool people, CancellationToken ct);
    Task<VeoOperationResult> PollAsync(string apiKey, string operationName, CancellationToken ct);
    Task<byte[]> DownloadAsync(string apiKey, string videoUri, CancellationToken ct);
}

public sealed record VeoOperationResult(bool Done, string? VideoUri, string? Failure);
