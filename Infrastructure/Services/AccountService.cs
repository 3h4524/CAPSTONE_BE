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

    public async Task<AccountInfoDto?> FindByGoogleIdAsync(
        string googleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);

        if (_cachedUser is not null && string.Equals(_cachedUser.OauthGoogleId, googleId, StringComparison.Ordinal))
        {
            return Map(_cachedUser);
        }

        _cachedUser = await accountRepository.FindByGoogleIdAsync(googleId, cancellationToken);

        return _cachedUser is null ? null : Map(_cachedUser);
    }

    public async Task<AccountCreationResultDto> CreateGoogleUserAsync(
        string email,
        string fullName,
        string googleId,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);

        var normalizedEmail = NormalizeEmail(email);

        if (await accountRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return AccountCreationResultDto.Failure("Email is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Google already verified the address, so the account is active immediately with no
        // password: it can only be signed into through Google.
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            PasswordHash = null,
            OauthGoogleId = googleId,
            OauthProvider = AuthConstants.GoogleProvider,
            AvatarUrl = avatarUrl,
            AccountStatus = AccountStatuses.Active,
            EmailVerified = true,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

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

    public async Task<AccountInfoDto?> LinkGoogleIdentityAsync(
        Guid userId,
        string googleId,
        string? avatarUrl,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleId);

        // Tracked on purpose: the change is staged so the caller commits it together with
        // issuing the session.
        var user = await accountRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var timestamp = utcNow.UtcDateTime;
        user.OauthGoogleId = googleId;
        user.OauthProvider ??= AuthConstants.GoogleProvider;
        user.AvatarUrl ??= avatarUrl;
        user.UpdatedAt = timestamp;

        // The address only reached this point because Google vouched for it, so an account that
        // registered with a password but never verified its email is confirmed here too.
        if (user.EmailVerified != true)
        {
            user.EmailVerified = true;
            user.EmailVerifiedAt = timestamp;

            if (string.Equals(user.AccountStatus, AccountStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase))
            {
                user.AccountStatus = AccountStatuses.Active;
            }
        }

        _cachedUser = user;
        return Map(user);
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

    public async Task<bool> ValidateCredentialsAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        cancellationToken.ThrowIfCancellationRequested();

        var user = _cachedUser is not null && _cachedUser.Id == userId
            ? _cachedUser
            : await accountRepository.GetByIdAsync(userId, cancellationToken);

        if (user?.PasswordHash is null)
        {
            return false;
        }

        _cachedUser = user;
        return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
            != PasswordVerificationResult.Failed;
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
