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
}
