using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Application.Abstractions.Persistence;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements account operations against the database-first Neon schema.
/// </summary>
public sealed class AccountService(
    IAccountRepository accountRepository,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
    : IAccountService
{
    private User? _cachedUser;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return accountRepository.EmailExistsAsync(NormalizeEmail(email), cancellationToken);
    }

    public async Task<AccountCreationResultDto> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var normalizedEmail = NormalizeEmail(email);

        // Answers the ordinary duplicate with a conflict result. A registration that races past
        // this check is caught by the partial unique index on the address in PostgreSQL.
        if (await accountRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return AccountCreationResultDto.Failure("Email is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The account starts unverified and inactive; redeeming the emailed verification token is
        // what confirms the address and activates it.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            AccountStatus = AccountStatuses.PendingVerification,
            EmailVerified = false,
            EmailVerifiedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        var role = await accountRepository.FindRoleByCodeAsync(AuthConstants.UserRole, cancellationToken);

        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid(),
                Code = AuthConstants.UserRole,
                Name = AuthConstants.UserRole,
                IsSystemRole = true,
                CreatedAt = now
            };
            accountRepository.AddRole(role);
        }

        await accountRepository.AddAsync(user, cancellationToken: cancellationToken);
        accountRepository.AddUserRole(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            GrantedAt = now
        });

        _cachedUser = user;
        return AccountCreationResultDto.Success(Map(user), [role.Code]);
    }

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

    public async Task<bool> ConfirmEmailAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        // Tracked on purpose: the change is staged so the caller commits it together with
        // spending the verification token, unlike the cached no-tracking lookups above.
        var user = await accountRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        var timestamp = utcNow.UtcDateTime;
        user.EmailVerified = true;
        user.EmailVerifiedAt = timestamp;
        user.UpdatedAt = timestamp;

        // Only an account held back for verification is promoted; a deliberate administrative
        // lock or suspension survives the email being confirmed.
        if (string.Equals(user.AccountStatus, AccountStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase))
        {
            user.AccountStatus = AccountStatuses.Active;
        }

        _cachedUser = user;
        return true;
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
