using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements account operations with ASP.NET Core Identity.
/// </summary>
/// <remarks>
/// Registered per request. Each lookup caches the resolved user for the lifetime of the request,
/// because a single use case calls several of these methods for the same account and the store
/// would otherwise re-read the same row every time.
/// </remarks>
public sealed class IdentityService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    RoleManager<Role> roleManager,
    TimeProvider timeProvider)
    : IIdentityService
{
    private User? _cachedUser;

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await userManager.FindByEmailAsync(email) is not null;
    }

    /// <inheritdoc />
    public async Task<IdentityCreateUserResult> CreateUserAsync(
        string email,
        string password,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new User
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            EmailVerifiedAtUtc = timeProvider.GetUtcNow(),
            AccountStatus = AccountStatuses.Active
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return IdentityCreateUserResult.Failure(Describe(createResult));
        }

        var roleResult = await EnsureRoleExistsAsync(AuthConstants.UserRole);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return IdentityCreateUserResult.Failure(Describe(roleResult));
        }

        var addToRoleResult = await userManager.AddToRoleAsync(user, AuthConstants.UserRole);
        if (!addToRoleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return IdentityCreateUserResult.Failure(Describe(addToRoleResult));
        }

        return IdentityCreateUserResult.Success(await MapUserAsync(user), [AuthConstants.UserRole]);
    }

    /// <inheritdoc />
    public async Task<IdentityUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cachedUser is not null && string.Equals(_cachedUser.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return await MapUserAsync(_cachedUser);
        }

        var user = await userManager.FindByEmailAsync(email);

        return user is null ? null : await MapUserAsync(user);
    }

    /// <inheritdoc />
    public async Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await GetUserAsync(userId);

        return user is null ? null : await MapUserAsync(user);
    }

    /// <inheritdoc />
    public async Task<CredentialValidationResult> ValidateCredentialsAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await GetUserAsync(userId);

        if (user is null)
        {
            return new CredentialValidationResult(false, false, false);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        return new CredentialValidationResult(result.Succeeded, result.IsLockedOut, result.IsNotAllowed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await GetUserAsync(userId);
        if (user is null)
        {
            return Array.Empty<string>();
        }

        var roles = await userManager.GetRolesAsync(user);
        return roles.ToArray();
    }

    /// <inheritdoc />
    public async Task TouchLastLoginAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await GetUserAsync(userId);
        if (user is null)
        {
            return;
        }

        user.LastLoginAtUtc = utcNow;
        await userManager.UpdateAsync(user);
    }

    private async Task<User?> GetUserAsync(Guid userId)
    {
        if (_cachedUser is not null && _cachedUser.Id == userId)
        {
            return _cachedUser;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            _cachedUser = user;
        }

        return user;
    }

    private async Task<IdentityResult> EnsureRoleExistsAsync(string roleCode)
    {
        if (await roleManager.RoleExistsAsync(roleCode))
        {
            return IdentityResult.Success;
        }

        var result = await roleManager.CreateAsync(new Role(roleCode)
        {
            DisplayName = roleCode,
            IsSystemRole = true
        });

        // A concurrent registration may have created it first, which is not a failure here.
        if (!result.Succeeded && await roleManager.RoleExistsAsync(roleCode))
        {
            return IdentityResult.Success;
        }

        return result;
    }

    private async Task<IdentityUserInfo> MapUserAsync(User user)
    {
        _cachedUser = user;

        var isLockedOut = await userManager.IsLockedOutAsync(user);
        var email = user.Email ?? user.UserName ?? string.Empty;

        return new IdentityUserInfo(user.Id, email, user.FullName, user.IsActive, isLockedOut);
    }

    private static IReadOnlyCollection<string> Describe(IdentityResult result) =>
        result.Errors.Select(error => error.Description).ToArray();
}
