namespace APCS.Application.Abstractions.AI;

/// <summary>
/// Passes an image provider's "the image is ready" call (a webhook) on to the request on this server that is waiting for
/// that image, so it asks the provider for the result at once instead of at its next status check.
/// </summary>
/// <remarks>
/// The call only wakes the waiting request: the result is always read from the provider with the server's own key, never
/// from the call. A call for an image nobody here waits for does nothing.
/// </remarks>
public interface IImageReadySignals
{
    /// <summary>The path Ideogram's "image ready" calls arrive at, after the server's public address.</summary>
    const string IdeogramWebhookPath = "api/webhooks/ideogram";

    /// <summary>Wakes the request waiting for this generation. Returns whether one was waiting on this server.</summary>
    bool Ready(string generationId);
}
