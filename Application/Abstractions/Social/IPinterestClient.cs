namespace APCS.Application.Abstractions.Social;

/// <summary>Tokens Pinterest issued for one connected seller.</summary>
/// <param name="AccessToken">Bearer token for the seller's account; lives about 30 days.</param>
/// <param name="RefreshToken">Exchanged for a new access token; null when Pinterest kept the previous one.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the seller must connect again; null when Pinterest did not say.</param>
public sealed record PinterestTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset? RefreshTokenExpiresAtUtc);

/// <summary>A board of the seller's Pinterest account.</summary>
/// <param name="Privacy"><c>PUBLIC</c>, <c>PROTECTED</c> or <c>SECRET</c>, as Pinterest names it.</param>
public sealed record PinterestBoard(string Id, string Name, string? Privacy);

/// <summary>An image pin to create; Pinterest downloads the image from <paramref name="ImageUrl"/>.</summary>
public sealed record PinterestNewPin(string BoardId, string ImageUrl, string? Title, string? Description, string? Link, string? AltText);

/// <summary>A pin Pinterest created.</summary>
public sealed record PinterestCreatedPin(string PinId, string Url);

/// <summary>Why a Pinterest call failed, as far as a use case needs to react.</summary>
public enum PinterestFailureKind
{
    /// <summary>The seller's grant is no longer valid: they must connect again.</summary>
    Unauthorized,
    /// <summary>Pinterest limited the request rate; retry later.</summary>
    RateLimited,
    /// <summary>Pinterest refused this request (a board that is gone, an image it cannot read).</summary>
    Rejected,
    /// <summary>Pinterest could not be reached or answered with an error.</summary>
    Unavailable,
}

/// <summary>A Pinterest call failed; carries no token or request value.</summary>
public sealed class PinterestApiException(PinterestFailureKind kind, string message, string? reason = null) : Exception(message)
{
    public PinterestFailureKind Kind { get; } = kind;

    /// <summary>Pinterest's explanation of a refused request (its <c>message</c>), safe to show the seller.</summary>
    public string? Reason { get; } = reason;
}

/// <summary>
/// Connects a seller's Pinterest account through OAuth 2.0 and posts pins to their boards.
/// Pinterest-specific HTTP, scopes and token storage format stay in the Infrastructure adapter.
/// </summary>
public interface IPinterestClient
{
    /// <summary>Whether a Pinterest app is configured; without it no seller can connect.</summary>
    bool IsConfigured { get; }

    /// <summary>The Pinterest consent page the seller is sent to.</summary>
    string BuildAuthorizationUrl(string state);

    /// <summary>The client page the seller returns to after the consent page, with the outcome.</summary>
    string BuildReturnUrl(string outcome);

    Task<PinterestTokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task<PinterestTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>The username of the token's owner.</summary>
    Task<string> GetUsernameAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>The boards the seller owns or may pin to.</summary>
    Task<IReadOnlyList<PinterestBoard>> GetBoardsAsync(string accessToken, CancellationToken cancellationToken);

    Task<PinterestCreatedPin> CreatePinAsync(string accessToken, PinterestNewPin pin, CancellationToken cancellationToken);

    /// <summary>Encrypts a token for storage.</summary>
    string Protect(string value);

    /// <summary>Decrypts a stored token.</summary>
    string Unprotect(string value);
}
