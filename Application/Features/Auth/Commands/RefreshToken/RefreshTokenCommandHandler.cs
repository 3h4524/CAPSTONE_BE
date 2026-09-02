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
    IRefreshTokenRepository refreshTokenRepository,
    IIdentityService identityService,
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

        var utcNow = timeProvider.GetUtcNow();
        var tokenHash = jwtService.HashRefreshToken(request.RefreshToken);
        var existingToken = await refreshTokenRepository.GetByHashAsync(tokenHash, cancellationToken);

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
            existingToken.Revoke(utcNow, "Expired");
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
            }

            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenExpired());
        }

        var user = await identityService.FindByIdAsync(existingToken.SellerId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenInvalid());
        }

        if (!user.IsActive)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.Inactive());
        }

        if (user.IsLockedOut)
        {
            return Result.Failure<RefreshTokenResponse>(AuthErrors.LockedOut());
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
        var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow);

        existingToken.Revoke(utcNow, "Rotated", session.RefreshTokenHash);
        refreshTokenRepository.Add(session.Entity);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
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
}
