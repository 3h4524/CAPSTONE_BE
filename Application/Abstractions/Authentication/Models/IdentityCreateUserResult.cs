namespace APCS.Application.Abstractions.Authentication.Models;

/// <summary>
/// Represents the outcome of creating a user, carrying the created user on success.
/// </summary>
/// <param name="Succeeded">Indicates whether the operation succeeded.</param>
/// <param name="Errors">The user-safe operation errors.</param>
/// <param name="User">The created user, present only on success.</param>
/// <param name="Roles">The roles assigned at creation, present only on success.</param>
public sealed record IdentityCreateUserResult(
    bool Succeeded,
    IReadOnlyCollection<string> Errors,
    IdentityUserInfo? User,
    IReadOnlyCollection<string> Roles)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static IdentityCreateUserResult Success(IdentityUserInfo user, IReadOnlyCollection<string> roles) =>
        new(true, Array.Empty<string>(), user, roles);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static IdentityCreateUserResult Failure(IReadOnlyCollection<string> errors) =>
        new(false, errors, null, Array.Empty<string>());
}
