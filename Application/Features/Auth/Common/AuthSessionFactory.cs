using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Authentication.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal static class AuthSessionFactory
{
    public static AuthSession Create(
        IJwtService jwtService,
        IdentityUserInfo user,
        IReadOnlyCollection<string> roles,
        DateTimeOffset utcNow)
    {
        var accessToken = jwtService.GenerateAccessToken(user.Id, user.Email, roles);
        var refreshToken = jwtService.GenerateRefreshToken();
        var refreshTokenHash = jwtService.HashRefreshToken(refreshToken);
        var refreshTokenExpiresAtUtc = jwtService.GetRefreshTokenExpiresAt(utcNow);
        var refreshTokenEntity = RefreshToken.Create(
            user.Id,
            refreshTokenHash,
            accessToken.JwtId,
            refreshTokenExpiresAtUtc,
            utcNow);

        return new AuthSession(accessToken, refreshToken, refreshTokenHash, refreshTokenExpiresAtUtc, refreshTokenEntity);
    }
}
