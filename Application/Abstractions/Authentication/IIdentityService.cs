using APCS.Application.Abstractions.Authentication.Models;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Abstracts identity account operations from application use cases.
/// </summary>
public interface IIdentityService
{
    /// <summary>
    /// Returns true when an email is already registered.
    /// </summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new application user.
    /// </summary>
    Task<IdentityOperationResult> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by email.
    /// </summary>
    Task<IdentityUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user by identifier.
    /// </summary>
    Task<IdentityUserInfo?> FindByIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates credentials and applies Identity lockout rules on failure.
    /// </summary>
    Task<CredentialValidationResult> ValidateCredentialsAsync(
        int userId,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user roles.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetRolesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user's last-login timestamp.
    /// </summary>
    Task TouchLastLoginAsync(int userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}
