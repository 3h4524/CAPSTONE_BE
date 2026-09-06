using APCS.Application.Abstractions.Authentication.Models;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Provides account operations without exposing persistence-specific user types.
/// </summary>
public interface IAccountService
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountCreationResult> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default);

    Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

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
