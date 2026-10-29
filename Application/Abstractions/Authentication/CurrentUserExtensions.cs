namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Reads the authenticated user identifier from the current request.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Returns the authenticated user identifier, or null for anonymous requests.
    /// </summary>
    public static Guid? TryGetUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        return currentUser.IsAuthenticated && currentUser.UserId is Guid userId ? userId : null;
    }
}
