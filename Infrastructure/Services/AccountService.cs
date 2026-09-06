using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements account operations against the database-first Neon schema.
/// </summary>
public sealed class AccountService(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider)
    : IAccountService
{
    private User? _cachedUser;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = NormalizeEmail(email);

        return dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    public async Task<AccountCreationResult> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var normalizedEmail = NormalizeEmail(email);
        if (await dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
        {
            return AccountCreationResult.Failure("Email is already registered.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            AccountStatus = AccountStatuses.Active,
            EmailVerified = true,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        var role = await dbContext.Roles
            .SingleOrDefaultAsync(existingRole => existingRole.Code == AuthConstants.UserRole, cancellationToken);

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
            dbContext.Roles.Add(role);
        }

        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            GrantedAt = now
        });

        _cachedUser = user;
        return AccountCreationResult.Success(Map(user), [role.Code]);
    }

    public async Task<AccountInfo?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = NormalizeEmail(email);

        if (_cachedUser is not null && string.Equals(_cachedUser.Email, normalizedEmail, StringComparison.Ordinal))
        {
            return Map(_cachedUser);
        }

        _cachedUser = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

        return _cachedUser is null ? null : Map(_cachedUser);
    }

    public async Task<AccountInfo?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_cachedUser is not null && _cachedUser.Id == userId)
        {
            return Map(_cachedUser);
        }

        _cachedUser = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

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
            : await dbContext.Users.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user?.PasswordHash is null)
        {
            return false;
        }

        _cachedUser = user;
        return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
            != PasswordVerificationResult.Failed;
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RevokedAt == null)
            .Select(userRole => userRole.Role.Code)
            .ToArrayAsync(cancellationToken);

    public async Task TouchLastLoginAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var timestamp = utcNow.UtcDateTime;
        await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.LastLoginAt, timestamp)
                    .SetProperty(user => user.UpdatedAt, timestamp),
                cancellationToken);

        if (_cachedUser is not null && _cachedUser.Id == userId)
        {
            _cachedUser.LastLoginAt = timestamp;
            _cachedUser.UpdatedAt = timestamp;
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static AccountInfo Map(User user) =>
        new(
            user.Id,
            user.Email,
            user.FullName,
            user.CanAuthenticate);
}
