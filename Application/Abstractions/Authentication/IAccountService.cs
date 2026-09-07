using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Provides account operations without exposing persistence-specific user types.
/// </summary>
public interface IAccountService
{
    Task<AccountInfoDto?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountInfoDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an account previously linked to the given Google subject identifier.
    /// </summary>
    Task<AccountInfoDto?> FindByGoogleIdAsync(string googleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new account created from a verified Google identity and its default role.
    /// </summary>
    /// <remarks>
    /// The email is trusted as verified because Google vouched for it, so the account is created
    /// active with no password: it can only be signed into through Google.
    /// </remarks>
    Task<AccountCreationResultDto> CreateGoogleUserAsync(
        string email,
        string fullName,
        string googleId,
        string? avatarUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a Google identity to an existing account, typically one that originally registered
    /// with a password under the same email address.
    /// </summary>
    /// <remarks>
    /// Stages the change on the current unit of work, so the caller commits it together with
    /// issuing the session.
    /// </remarks>
    Task<AccountInfoDto?> LinkGoogleIdentityAsync(
        Guid userId,
        string googleId,
        string? avatarUrl,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateCredentialsAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task TouchLastLoginAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
