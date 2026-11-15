namespace APCS.Application.Features.Social.Dtos.Response;

/// <summary>The Pinterest consent page to send the seller to.</summary>
public sealed record StartPinterestConnectionResponseDto(string AuthorizationUrl);

/// <summary>Where the callback sends the browser, and the outcome it carries (see <see cref="Common.PinterestConnectionOutcomes"/>).</summary>
public sealed record CompletePinterestConnectionResponseDto(string RedirectUrl, string Outcome);

/// <summary>The seller's Pinterest connection.</summary>
/// <param name="Configured">False when this server has no Pinterest app: nobody can connect.</param>
/// <param name="Status"><c>not_connected</c>, <c>connected</c> or <c>reconnect_required</c>.</param>
public sealed record PinterestConnectionResponseDto(
    bool Configured,
    string Status,
    string? Username,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? ReconnectByUtc);

/// <summary>A board the seller can pin to.</summary>
public sealed record PinterestBoardDto(string Id, string Name, string? Privacy);
