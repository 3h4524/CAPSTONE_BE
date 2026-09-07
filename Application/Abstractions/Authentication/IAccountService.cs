using APCS.Application.Abstractions.Authentication.Dtos;

namespace APCS.Application.Abstractions.Authentication;

/// <summary>
/// Provides account operations without exposing persistence-specific user types.
/// </summary>
public interface IAccountService
{
    Task<AccountInfoDto?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<AccountInfoDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task TouchLastLoginAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
