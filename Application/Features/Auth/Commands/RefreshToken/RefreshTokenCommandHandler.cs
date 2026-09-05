using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Auth.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Handles access token refresh and refresh token rotation.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IUnitOfWork unitOfWork,
    IAuthTokenRepository authTokenRepository,
    IIdentityService identityService,
    IJwtService jwtService,
    TimeProvider timeProvider)
    : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    private const int MaxRotationChainDepth = 256;

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
            // Someone replayed a token that was already rotated away. The legitimate holder now
            // has a descendant of this token, and we cannot tell which party is which, so the
            // whole rotation chain is killed rather than just rejecting this one request.
            await RevokeRotationChainAsync(existingToken, utcNow, context, cancellationToken);
            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
        }

        if (existingToken.IsExpired(utcNow))
        {
            existingToken.Revoke(utcNow, "Expired", revokedByIp: context.IpAddress);
            if (!await TrySaveAsync(cancellationToken))
            {
                return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenReused());
            }

            return Result.Failure<RefreshTokenResponse>(AuthErrors.RefreshTokenExpired());
        }

        var user = await identityService.FindByIdAsync(existingToken.UserId, cancellationToken);
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
        var session = AuthSessionFactory.Create(jwtService, user, roles, utcNow, context);

        existingToken.Revoke(utcNow, "Rotated", session.RefreshTokenHash, context.IpAddress);
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

    /// <summary>
    /// Walks forward through <see cref="AuthToken.ReplacedByTokenHash"/> and revokes every token
    /// descended from the replayed one.
    /// </summary>
    private async Task RevokeRotationChainAsync(
        AuthToken replayedToken,
        DateTimeOffset utcNow,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var nextHash = replayedToken.ReplacedByTokenHash;
        var revokedAny = false;

        // A chain is only ever as long as the number of rotations in one session's lifetime.
        // The bound stops corrupted data with a cycle in it from spinning here forever.
        for (var depth = 0; nextHash is not null && depth < MaxRotationChainDepth; depth++)
        {
            var descendant = await authTokenRepository.GetByHashAsync(nextHash, cancellationToken);
            if (descendant is null)
            {
                break;
            }

            // Every token in the chain except the newest is already revoked as "Rotated", so the
            // walk has to pass straight through them to reach the one that is still usable.
            if (!descendant.IsRevoked)
            {
                descendant.Revoke(utcNow, "ReuseDetected", revokedByIp: context.IpAddress);
                revokedAny = true;
            }

            nextHash = descendant.ReplacedByTokenHash;
        }

        if (revokedAny)
        {
            // Best effort: if a concurrent request already revoked the chain the outcome is the
            // same, and the caller is rejected either way.
            await TrySaveAsync(cancellationToken);
        }
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
