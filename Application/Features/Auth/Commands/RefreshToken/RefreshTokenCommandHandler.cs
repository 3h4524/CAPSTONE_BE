using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Handles access token refresh and refresh token rotation.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IAccountService accountService,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    /// <inheritdoc />
    public async Task<Result<RefreshTokenResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenMissing());
        }

        var context = request.Context ?? RequestContext.None;
        var utcNow = timeProvider.GetUtcNow();
        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var existingToken = await authTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

        if (existingToken is null)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenInvalid());
        }

        if (existingToken.IsRevoked)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
        }

        if (existingToken.IsExpired(utcNow))
        {
            existingToken.Revoke(utcNow);
            if (!await TrySaveAsync(cancellationToken))
            {
                return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
            }

            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenExpired());
        }

        var user = await accountService.FindByIdAsync(existingToken.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenInvalid());
        }

        if (!user.IsActive)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.Inactive());
        }

        var roles = await accountService.GetRolesAsync(user.Id, cancellationToken);
        var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow, context);

        existingToken.Revoke(utcNow);
        authTokenRepository.Add(session.Entity);

        if (!await TrySaveAsync(cancellationToken))
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
        }

        var response = new RefreshTokenResponse(
            session.AccessToken.AccessToken,
            session.AccessToken.ExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc);

        return Result.Success(response);
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
