using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal static class AuthSessionFactory
{
    /// <summary>
    /// Issues an access token plus the refresh token that will rotate it.
    /// </summary>
    /// <remarks>
    /// The caller's IP and user agent are recorded on the refresh token so a stolen-token
    /// replay can be traced back to the network it came from.
    /// </remarks>
    public static AuthSession Create(
        IJwtService jwtService,
        AccountInfo user,
        IReadOnlyCollection<string> roles,
        DateTimeOffset utcNow,
        RequestContext requestContext)
    {
        var accessToken = jwtService.GenerateAccessToken(user.Id, user.Email, roles);
        var refreshToken = jwtService.GenerateRefreshToken();
        var refreshTokenHash = jwtService.HashRefreshToken(refreshToken);
        var refreshTokenExpiresAtUtc = jwtService.GetRefreshTokenExpiresAt(utcNow);
        var refreshTokenEntity = AuthToken.CreateRefreshToken(
            user.Id,
            refreshTokenHash,
            refreshTokenExpiresAtUtc,
            utcNow,
            requestContext.IpAddress,
            requestContext.UserAgent);

        return new AuthSession(accessToken, refreshToken, refreshTokenHash, refreshTokenExpiresAtUtc, refreshTokenEntity);
    }
}
