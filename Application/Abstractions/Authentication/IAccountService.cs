using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Provides account operations without exposing persistence-specific user types.
/// </summary>
public interface IAccountService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new account and its default role on the current unit of work.
    /// </summary>
    /// <remarks>
    /// Nothing is written until the caller saves. Email uniqueness is owned by the partial unique
    /// index in PostgreSQL; the duplicate check here only turns the ordinary case into a conflict
    /// result rather than a constraint violation.
    /// </remarks>
    Task<AccountCreationResultDto> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Marks the account email as verified and activates an account awaiting verification.
    /// </summary>
    /// <remarks>
    /// Stages the change on the current unit of work, so the caller commits it together with
    /// spending the verification token.
    /// </remarks>
    /// <returns><see langword="true"/> when the account was found and updated.</returns>
    Task<bool> ConfirmEmailAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the account's password hash.
    /// </summary>
    /// <remarks>
    /// Stages the change on the current unit of work; the caller must save.
    /// </remarks>
    /// <returns><see langword="true"/> when the user was found and updated.</returns>
    Task<bool> UpdatePasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies whether the supplied plain-text password matches the stored hash.
    /// </summary>
    Task<bool> VerifyPasswordAsync(
        Guid userId,
        string candidatePassword,
        CancellationToken cancellationToken = default);
}
