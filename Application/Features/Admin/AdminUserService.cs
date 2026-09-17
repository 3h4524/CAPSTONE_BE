using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.Dtos.Request;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Admin;

public sealed class AdminUserService(
    IRepository<User> userRepository,
    IRepository<SubscriptionPlan> planRepository,
    IUnitOfWork unitOfWork,
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

        var now = timeProvider.GetUtcNow().DateTime;
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

        var now = timeProvider.GetUtcNow().DateTime;
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
            CreatedAt: user.CreatedAt
        );

        return Result<AdminUserDto>.Success(dto);
    }

    public async Task<Result<bool>> SuspendUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("User.NotFound", "User not found."));

        user.AccountStatus = "Suspended";
        user.UpdatedAt = timeProvider.GetUtcNow().DateTime;

        await userRepository.UpdateAsync(user, false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} suspended by admin.", id);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UnlockUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<bool>.Failure(Error.NotFound("User.NotFound", "User not found."));

        user.AccountStatus = "Active";
        user.UpdatedAt = timeProvider.GetUtcNow().DateTime;

        await userRepository.UpdateAsync(user, false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} unlocked by admin.", id);
        return Result<bool>.Success(true);
    }
}
