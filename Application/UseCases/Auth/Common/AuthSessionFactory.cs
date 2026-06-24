using APCS.Application.Common.Interfaces;
using APCS.Application.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.UseCases.Auth.Common;

internal static class AuthSessionFactory
{
    public static AuthSession Create(
        IJwtService jwtService,
        IdentityUserInfo user,
        IReadOnlyCollection<string> roles,
        DateTimeOffset utcNow,
        string? ipAddress)
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
            utcNow,
            ipAddress);

        return new AuthSession(accessToken, refreshToken, refreshTokenHash, refreshTokenExpiresAtUtc, refreshTokenEntity);
    }
}
