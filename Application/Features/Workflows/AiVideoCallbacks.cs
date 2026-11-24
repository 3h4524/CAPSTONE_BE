using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// Where the video provider calls back when an AI scene changes state. Bound to <c>AiVideo:Callback</c>. Off while either
/// value is empty (a machine with no public address, such as a developer's): the scenes are then only asked about, as before.
/// </summary>
public sealed class AiVideoCallbackSettings
{
    public const string SectionName = "AiVideo:Callback";

    /// <summary>The public HTTPS origin the provider reaches the API at, e.g. <c>https://example.com</c>; the path is added.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The key the callback URLs are signed with. Changing it voids the URLs of scenes already submitted: their
    /// calls are refused, and those scenes are found by asking about them instead.</summary>
    public string? Secret { get; set; }

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Secret) &&
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// The callback URL of one AI scene, and the check that a call really came to a URL this API handed out. The provider does
/// not sign what it sends, so the URL carries a signature of the job and scene it is for: a call to any other address is
/// refused before anything is read from the database.
/// </summary>
public sealed class AiVideoCallbackUrls(IOptions<AiVideoCallbackSettings> options)
{
    /// <summary>The path the callbacks arrive at, before the job, scene and signature.</summary>
    public const string RoutePrefix = "api/webhooks/minimax";

    /// <summary>The URL to give the provider for this scene, or null when callbacks are off.</summary>
    public string? For(Guid jobId, int sceneOrder)
    {
        var settings = options.Value;
        if (!settings.Enabled) return null;
        return $"{settings.BaseUrl!.TrimEnd('/')}/{RoutePrefix}/{jobId:N}/{sceneOrder}/{Sign(settings.Secret!, jobId, sceneOrder)}";
    }

    /// <summary>Whether the signature is the one this API gave the scene. False when callbacks are off.</summary>
    public bool Verify(Guid jobId, int sceneOrder, string? signature)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrEmpty(signature)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(settings.Secret!, jobId, sceneOrder));
        var given = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string Sign(string secret, Guid jobId, int sceneOrder)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"ai-scene:{jobId:N}:{sceneOrder}"));
        // Base64url without padding: safe in a path segment.
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>What to answer a provider's callback.</summary>
public enum AiVideoCallbackOutcome
{
    /// <summary>Not a URL this API handed out: answered as if there were nothing there.</summary>
    Unknown,
    /// <summary>The provider checks the URL before using it: its challenge is sent back as it came.</summary>
    Challenge,
    /// <summary>Read; whatever it said is looked into by asking the provider, never taken from the call.</summary>
    Accepted
}

public sealed record AiVideoCallbackResult(AiVideoCallbackOutcome Outcome, string? Challenge = null);

public interface IAiVideoCallbackService
{
    /// <summary>Handles one call the video provider made to the callback URL of an AI scene.</summary>
    Task<AiVideoCallbackResult> ReceiveAsync(Guid jobId, int sceneOrder, string? signature, string body, CancellationToken ct);
}

/// <summary>
/// Takes MiniMax's calls about the AI scenes the API makes (<see cref="AiClipPhaseService"/>). A call that says a scene
/// finished only marks it to be asked about at once (<see cref="IAiShotJobService.AnnounceClipAsync"/>), which wakes the
/// background pass: what the provider answers then is what counts, so a forged, repeated or late call changes nothing.
/// </summary>
/// <remarks>
/// MiniMax first sends a <c>challenge</c> that must come back within three seconds, so that answer reads nothing but the
/// body. Its later calls have the body of its task query: <c>{"task": {"id", "status", ...}}</c>.
/// </remarks>
public sealed class AiVideoCallbackService(AiVideoCallbackUrls urls, IAiShotJobService shots,
    ILogger<AiVideoCallbackService>? logger = null) : IAiVideoCallbackService
{
    /// <summary>Task statuses after which nothing more happens to the scene at the provider.</summary>
    private static readonly HashSet<string> Finished = new(StringComparer.OrdinalIgnoreCase) { "succeeded", "success", "failed", "cancelled" };

    public async Task<AiVideoCallbackResult> ReceiveAsync(Guid jobId, int sceneOrder, string? signature, string body, CancellationToken ct)
    {
        if (sceneOrder < 0 || !urls.Verify(jobId, sceneOrder, signature)) return new(AiVideoCallbackOutcome.Unknown);

        string? challenge = null, taskId = null, status = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                challenge = Text(root, "challenge");
                // The task query's shape; the older API put the id and status at the top.
                var task = root.TryGetProperty("task", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;
                taskId = Text(task, "id") ?? Text(task, "task_id");
                status = Text(task, "status");
            }
        }
        catch (JsonException)
        {
            logger?.LogWarning("Video provider callback for scene {Scene} of job {Job} had no readable body", sceneOrder, jobId);
            return new(AiVideoCallbackOutcome.Accepted);
        }

        if (challenge is not null) return new(AiVideoCallbackOutcome.Challenge, challenge);
        if (taskId is null || status is null || !Finished.Contains(status)) return new(AiVideoCallbackOutcome.Accepted);

        var announced = await shots.AnnounceClipAsync(jobId, sceneOrder, taskId, ct);
        if (announced.IsFailure || !announced.Value)
            logger?.LogInformation("Video provider callback for scene {Scene} of job {Job} ({Status}) matched no scene being made",
                sceneOrder, jobId, status);
        return new(AiVideoCallbackOutcome.Accepted);
    }

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
