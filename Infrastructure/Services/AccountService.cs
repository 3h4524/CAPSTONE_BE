using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements account operations against the database-first Neon schema.
/// </summary>
public sealed class AccountService(IAccountRepository accountRepository) : IAccountService
{
    private User? _cachedUser;

    public async Task<AccountInfoDto?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = NormalizeEmail(email);

        if (_cachedUser is not null && string.Equals(_cachedUser.Email, normalizedEmail, StringComparison.Ordinal))
        {
            return Map(_cachedUser);
        }

        _cachedUser = await accountRepository.FindByEmailAsync(normalizedEmail, cancellationToken);

        return _cachedUser is null ? null : Map(_cachedUser);
    }

    public async Task<AccountInfoDto?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_cachedUser is not null && _cachedUser.Id == userId)
        {
            return Map(_cachedUser);
        }

        _cachedUser = await accountRepository.GetByIdAsync(userId, cancellationToken);

        return _cachedUser is null ? null : Map(_cachedUser);
    }

    public Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        accountRepository.GetActiveRoleCodesAsync(userId, cancellationToken);

    public async Task TouchLastLoginAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        await accountRepository.TouchLastLoginAsync(userId, utcNow, cancellationToken);

        if (_cachedUser is not null && _cachedUser.Id == userId)
        {
            var timestamp = utcNow.UtcDateTime;
            _cachedUser.LastLoginAt = timestamp;
            _cachedUser.UpdatedAt = timestamp;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static AccountInfoDto Map(User user) =>
        new(
            user.Id,
            user.Email,
            user.FullName,
            user.CanAuthenticate,
            user.EmailVerified == true);
}
