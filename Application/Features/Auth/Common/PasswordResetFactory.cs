using APCS.Application.Abstractions.Authentication;
using APCS.Common.Constants;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal static class PasswordResetFactory
{
    /// <summary>
    /// Issues a single-use password reset token for an account.
    /// </summary>
    /// <remarks>
    /// Shared by forgot-password and future admin-initiated resets so both produce the same
    /// token shape and lifetime. The caller's IP and user agent are recorded for the same
    /// audit reason as on a refresh token.
    /// </remarks>
    public static PasswordReset Create(
        IJwtService jwtService,
        Guid userId,
        DateTimeOffset utcNow,
        RequestContext requestContext)
    {
        var token = jwtService.GeneratePasswordResetToken();
        var tokenHash = jwtService.HashPasswordResetToken(token);
        var expiresAtUtc = jwtService.GetPasswordResetExpiresAt(utcNow);
        var entity = AuthToken.CreateSingleUseToken(
            userId,
            AuthTokenTypes.PasswordReset,
            tokenHash,
            expiresAtUtc,
            utcNow,
            requestContext.IpAddress,
            requestContext.UserAgent);

        return new PasswordReset(token, entity);
    }
}
