using APCS.Application.Abstractions.Authentication;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;

namespace APCS.Application.Features.Auth.Queries.GetCurrentUser;

/// <summary>
/// Handles current user lookups.
/// </summary>
public sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IAccountService accountService)
    : IRequestHandler<GetCurrentUserQuery, Result<AuthenticatedUserResponse>>
{
    /// <inheritdoc />
    public async Task<Result<AuthenticatedUserResponse>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Result.Failure<AuthenticatedUserResponse>(AuthErrors.Unauthenticated());
        }

        var user = await accountService.FindByIdAsync(currentUser.UserId.Value, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AuthenticatedUserResponse>(AuthErrors.UserNotFound());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        return Result.Success(new AuthenticatedUserResponse(user.Id, user.Email, user.FullName, roles));
    }
}
