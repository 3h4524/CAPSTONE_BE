using APCS.Application.Features.Admin.Dtos.Request;
using APCS.Application.Features.Admin.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Admin;

public interface IAdminUserService
{
    Task<Result<PagedResult<AdminUserDto>>> GetUsersAsync(GetUsersRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<string>>> GetAvailablePlanNamesAsync(CancellationToken cancellationToken = default);
    Task<Result<AdminUserDto>> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<bool>> SuspendUserAsync(Guid id, SuspendUserRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<bool>> UnlockUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminUserDto>> UpdateUserAsync(Guid id, UpdateAdminUserDto request, CancellationToken cancellationToken = default);
    Task<Result<bool>> SendResetPasswordLinkAsync(Guid id, CancellationToken cancellationToken = default);
}
