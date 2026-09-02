namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Provides information about the authenticated user for the current request.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Gets the current user identifier.
    /// </summary>
    int? UserId { get; }

    /// <summary>
    /// Gets the current user email.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets a value indicating whether the request is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
}
