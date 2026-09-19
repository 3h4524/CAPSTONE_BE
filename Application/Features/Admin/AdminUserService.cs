using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Admin.Dtos.Request;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Application.Features.Auth;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Admin;

public sealed class AdminUserService(
    IRepository<User> userRepository,
    IRepository<SubscriptionPlan> planRepository,
    IRepository<Role> roleRepository,
    IRepository<UserRole> userRoleRepository,
    IRepository<AuthToken> authTokenRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAuthService authService,
    TimeProvider timeProvider,
    ILogger<AdminUserService> logger)
    : IAdminUserService
{
    public async Task<Result<PagedResult<AdminUserDto>>> GetUsersAsync(GetUsersRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = userRepository.Query()
            .Include(u => u.UserRoleUsers).ThenInclude(ur => ur.Role)
            .Include(u => u.Subscriptions).ThenInclude(s => s.Plan)
            .Include(u => u.BatchJobs)
            .Include(u => u.Invoices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var search = request.SearchTerm.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search) ||
                u.Id.ToString().ToLower() == search);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && request.Status.ToLower() != "all")
        {
            query = query.Where(u => u.AccountStatus.ToLower() == request.Status.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role.ToLower() != "all")
        {
            query = query.Where(u => u.UserRoleUsers.Any(ur => ur.Role.Name.ToLower() == request.Role.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(request.Plan) && request.Plan.ToLower() != "all")
        {
            if (request.Plan.ToLower() == "free")
            {
                query = query.Where(u => !u.Subscriptions.Any(s => s.Status.ToLower() == "active"));
            }
            else
            {
                query = query.Where(u => u.Subscriptions.Any(s => s.Status.ToLower() == "active" && s.Plan.Name.ToLower() == request.Plan.ToLower()));
            }
        }

        if (request.SortBy?.ToLower() == "latestjoined")
        {
            query = request.SortDesc ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt);
        }
        else if (request.SortBy?.ToLower() == "name")
        {
            query = request.SortDesc ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName);
        }
        else
        {
            query = query.OrderByDescending(u => u.CreatedAt);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var users = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfMonth = new DateOnly(now.Year, now.Month, 1);

        var dtos = users.Select(u => new AdminUserDto(
            Id: u.Id,
            FullName: u.FullName,
            Email: u.Email,
            AvatarUrl: u.AvatarUrl,
            AccountStatus: u.AccountStatus,
            Roles: u.UserRoleUsers.Select(ur => ur.Role.Name).ToList(),
            Plan: u.Subscriptions.FirstOrDefault(s => s.Status.ToLower() == "active")?.Plan?.Name ?? "Free",
            TotalJobs: u.BatchJobs.Count,
            MonthlyApiCost: u.Invoices.Where(i => i.InvoiceDate >= startOfMonth && i.Status.ToLower() == "paid").Sum(i => i.TotalAmount),
            Birthday: u.Birthday,
            CreatedAt: u.CreatedAt
        )).ToList();

        return Result.Success(new PagedResult<AdminUserDto>(
            dtos,
            totalCount,
            request.PageIndex,
            request.PageSize));
    }

    public async Task<Result<IReadOnlyList<string>>> GetAvailablePlanNamesAsync(CancellationToken cancellationToken = default)
    {
        var plans = await planRepository.Query()
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<string>>(plans);
    }

    public async Task<Result<AdminUserDto>> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.Query()
            .Include(u => u.UserRoleUsers).ThenInclude(ur => ur.Role)
            .Include(u => u.Subscriptions).ThenInclude(s => s.Plan)
            .Include(u => u.BatchJobs)
            .Include(u => u.Invoices)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
            return Result<AdminUserDto>.Failure(Error.NotFound("User.NotFound", "User not found."));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfMonth = new DateOnly(now.Year, now.Month, 1);

        var dto = new AdminUserDto(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            AvatarUrl: user.AvatarUrl,
            AccountStatus: user.AccountStatus,
            Roles: user.UserRoleUsers.Select(ur => ur.Role.Name).ToList(),
            Plan: user.Subscriptions.FirstOrDefault(s => s.Status.ToLower() == "active")?.Plan?.Name ?? "Free",
            TotalJobs: user.BatchJobs.Count,
            MonthlyApiCost: user.Invoices.Where(i => i.InvoiceDate >= startOfMonth && i.Status.ToLower() == "paid").Sum(i => i.TotalAmount),
            Birthday: user.Birthday,
            CreatedAt: user.CreatedAt
        );

        return Result<AdminUserDto>.Success(dto);
    }

    public async Task<Result<bool>> SuspendUserAsync(Guid id, SuspendUserRequestDto request, CancellationToken cancellationToken = default)
    {
        if (id == currentUser.UserId)
            return Result<bool>.Failure(Error.Validation("You cannot suspend your own account."));

        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("User.NotFound", "User not found."));

        user.AccountStatus = "suspended";
        user.SuspendedUntil = request.DurationDays.HasValue ? timeProvider.GetUtcNow().UtcDateTime.AddDays(request.DurationDays.Value) : null;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Revoke tokens
        var tokens = await authTokenRepository.Query().Where(t => t.UserId == user.Id && t.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(timeProvider.GetUtcNow());
            await authTokenRepository.UpdateAsync(token, false, cancellationToken);
        }

        // Add Audit Log
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            ActionType = "SuspendUser",
            ResourceType = "User",
            ResourceId = user.Id,
            OldValue = "active",
            NewValue = request.DurationDays.HasValue ? $"suspended_until: {user.SuspendedUntil} (Reason: {request.Reason})" : $"permanent (Reason: {request.Reason})",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        user.AuditLogs.Add(auditLog);

        await userRepository.UpdateAsync(user, false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} suspended by admin. Reason: {Reason}", id, request.Reason);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UnlockUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("User.NotFound", "User not found."));

        user.AccountStatus = "active";
        user.SuspendedUntil = null;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        // Add Audit Log
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            ActionType = "UnlockUser",
            ResourceType = "User",
            ResourceId = user.Id,
            OldValue = "suspended",
            NewValue = "active",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        user.AuditLogs.Add(auditLog);

        await userRepository.UpdateAsync(user, false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} unlocked by admin.", id);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SendResetPasswordLinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("User.NotFound", "User not found."));

        var result = await authService.ForgotPasswordAsync(
            new ForgotPasswordRequestDto(user.Email, null),
            cancellationToken);

        if (result.IsFailure)
            return Result<bool>.Failure(result.Error);

        logger.LogInformation("Sent reset password link to {Email} by admin.", user.Email);
        return Result<bool>.Success(true);
    }

    public async Task<Result<AdminUserDto>> UpdateUserAsync(Guid id, UpdateAdminUserDto request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.Query()
            .Include(u => u.UserRoleUsers)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.Subscriptions)
                .ThenInclude(s => s.Plan)
            .Include(u => u.Invoices)
            .Include(u => u.BatchJobs)
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (user is null)
            return Result<AdminUserDto>.Failure(Error.NotFound("User.NotFound", "User not found."));

        if (id == currentUser.UserId)
        {
            var newRoleNamesList = request.Roles?.Select(r => r.ToLower()).ToList() ?? new List<string>();
            if (!newRoleNamesList.Contains("admin") && user.UserRoleUsers.Any(ur => ur.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            {
                return Result<AdminUserDto>.Failure(Error.Validation("You cannot remove your own Admin role."));
            }

            if (!request.AccountStatus.Equals(user.AccountStatus, StringComparison.OrdinalIgnoreCase))
            {
                return Result<AdminUserDto>.Failure(Error.Validation("You cannot change your own account status."));
            }
        }

        // Validate Email uniqueness
        if (!string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailExists = await userRepository.Query().AnyAsync(u => u.Email == request.Email, cancellationToken);
            if (emailExists)
                return Result<AdminUserDto>.Failure(Error.Conflict("User.EmailExists", "Email address already in use."));
        }

        user.FullName = request.FullName;
        user.Email = request.Email;

        if (request.Birthday.HasValue)
        {
            var today = timeProvider.GetUtcNow().Date;
            if (request.Birthday.Value.Date >= today)
            {
                return Result<AdminUserDto>.Failure(Error.Validation("Birthday must be in the past."));
            }
        }
        
        user.Birthday = request.Birthday.HasValue ? DateTime.SpecifyKind(request.Birthday.Value, DateTimeKind.Utc) : null;
        user.AvatarUrl = request.AvatarUrl;
        
        // Cập nhật roles
        var existingRoleNames = user.UserRoleUsers.Select(ur => ur.Role.Name).ToList();
        var newRoleNames = request.Roles.Select(r => r.ToLower()).Distinct().ToList();
        
        var rolesToRemove = user.UserRoleUsers.Where(ur => !newRoleNames.Contains(ur.Role.Name.ToLower())).ToList();
        foreach (var roleToRemove in rolesToRemove)
        {
            await userRoleRepository.RemoveAsync(roleToRemove, false, cancellationToken);
            user.UserRoleUsers.Remove(roleToRemove);
        }

        var allRoles = await roleRepository.Query().ToListAsync(cancellationToken);

        foreach (var roleName in newRoleNames)
        {
            if (!existingRoleNames.Any(r => r.ToLower() == roleName))
            {
                var role = allRoles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase));
                if (role != null)
                {
                    await userRoleRepository.AddAsync(new UserRole { UserId = user.Id, RoleId = role.Id }, false, cancellationToken);
                }
            }
        }

        // Account status and Auth tokens
        if (request.AccountStatus.Equals("Suspended", StringComparison.OrdinalIgnoreCase) && !user.AccountStatus.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            // Trạng thái mới là Suspended, cắt toàn bộ session
            var tokens = await authTokenRepository.Query().Where(t => t.UserId == user.Id && t.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime && t.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(timeProvider.GetUtcNow());
                await authTokenRepository.UpdateAsync(token, false, cancellationToken);
            }
        }
        
        user.AccountStatus = request.AccountStatus.ToLower();
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        
        // Return updated DTO
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfMonth = new DateOnly(now.Year, now.Month, 1);
        
        var resultRoles = allRoles
            .Where(r => newRoleNames.Contains(r.Name.ToLower()))
            .Select(r => r.Name)
            .ToList();

        var dto = new AdminUserDto(
            Id: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            AvatarUrl: user.AvatarUrl,
            AccountStatus: user.AccountStatus,
            Roles: resultRoles,
            Plan: user.Subscriptions.FirstOrDefault(s => s.Status.ToLower() == "active")?.Plan?.Name ?? "Free",
            TotalJobs: user.BatchJobs.Count,
            MonthlyApiCost: user.Invoices.Where(i => i.InvoiceDate >= startOfMonth && i.Status.ToLower() == "paid").Sum(i => i.TotalAmount),
            Birthday: user.Birthday,
            CreatedAt: user.CreatedAt
        );

        return Result<AdminUserDto>.Success(dto);
    }
}
