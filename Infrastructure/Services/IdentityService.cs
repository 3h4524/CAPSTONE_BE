using APCS.Application.Common.Interfaces;
using APCS.Application.Common.Models;
using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Implements account operations with ASP.NET Core Identity.
/// </summary>
public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    TimeProvider timeProvider)
    : IIdentityService
{
    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await userManager.FindByEmailAsync(email) is not null;
    }

    /// <inheritdoc />
    public async Task<IdentityOperationResult> CreateUserAsync(
        string email,
        string password,
        string? fullName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return ToOperationResult(createResult);
        }

        var roleResult = await EnsureRoleExistsAsync(AuthConstants.UserRole);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return roleResult;
        }

        var addToRoleResult = await userManager.AddToRoleAsync(user, AuthConstants.UserRole);
        if (!addToRoleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return ToOperationResult(addToRoleResult);
        }

        return IdentityOperationResult.Success();
    }

    /// <inheritdoc />
    public async Task<IdentityUserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByEmailAsync(email);

        return user is null ? null : await MapUserAsync(user);
    }

    /// <inheritdoc />
    public async Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());

        return user is null ? null : await MapUserAsync(user);
    }

    /// <inheritdoc />
    public async Task<CredentialValidationResult> ValidateCredentialsAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(userId.ToString());

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
        var user = await userManager.FindByIdAsync(userId.ToString());
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
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        user.LastLoginAtUtc = utcNow;
        user.UpdatedAtUtc = utcNow;
        await userManager.UpdateAsync(user);
    }

    private async Task<IdentityOperationResult> EnsureRoleExistsAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return IdentityOperationResult.Success();
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)
        {
            Id = Guid.NewGuid()
        });

        if (!result.Succeeded && await roleManager.RoleExistsAsync(roleName))
        {
            return IdentityOperationResult.Success();
        }

        return ToOperationResult(result);
    }

    private async Task<IdentityUserInfo> MapUserAsync(ApplicationUser user)
    {
        var isLockedOut = await userManager.IsLockedOutAsync(user);
        var email = user.Email ?? user.UserName ?? string.Empty;

        return new IdentityUserInfo(user.Id, email, user.FullName, user.IsActive, isLockedOut);
    }

    private static IdentityOperationResult ToOperationResult(IdentityResult result)
    {
        return result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failure(result.Errors.Select(error => error.Description).ToArray());
    }
}
