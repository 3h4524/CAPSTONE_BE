using APCS.Common.Models;

namespace APCS.Application.Features.Social.Common;

/// <summary>Creates expected errors returned by social use cases.</summary>
public static class SocialErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized("social.unauthorized", "Please sign in to manage your social accounts.");

    public static Error PinterestNotConfigured() =>
        Error.Failure("social.pinterest_not_configured", "Pinterest is not configured on this server yet. Contact the administrator.");

    public static Error PinterestNotConnected() =>
        Error.NotFound("social.pinterest_not_connected", "No Pinterest account is connected. Connect one on Push to media first.");

    public static Error PinterestReconnectRequired() =>
        Error.Conflict("social.pinterest_reconnect_required", "Pinterest no longer accepts this connection. Connect your Pinterest account again on Push to media.");

    public static Error PinterestUnavailable() =>
        Error.Failure("social.pinterest_unavailable", "Pinterest is not answering right now. Try again in a moment.");

    public static Error PinterestRateLimited() =>
        Error.Failure("social.pinterest_rate_limited", "Pinterest is limiting requests. Try again in a few minutes.");
}

/// <summary>What the Pinterest callback reports to the client page.</summary>
public static class PinterestConnectionOutcomes
{
    public const string Connected = "connected";
    public const string Denied = "denied";
    public const string Expired = "expired";
    public const string Failed = "failed";
}

/// <summary>Statuses of a seller's Pinterest connection.</summary>
public static class PinterestConnectionStatuses
{
    public const string NotConnected = "not_connected";
    public const string Connected = "connected";
    public const string ReconnectRequired = "reconnect_required";
}

/// <summary>An image pin to post: a mock-up, the image link Pinterest downloads, and the pin's text.</summary>
public sealed record PinToPost(Guid MockupImageId, string ImageUrl, string? Title, string? Description, string? Link, string? AltText);

/// <summary>A pin on the board: just created, or posted by an earlier attempt of the same step.</summary>
public sealed record PostedPin(Guid MockupImageId, string PinId, string Url, bool AlreadyPosted);

/// <summary>What posting to a board did: the pins there, and why the others are not.</summary>
public sealed record PostPinsResult(IReadOnlyList<PostedPin> Posted, IReadOnlyList<string> Failures);
