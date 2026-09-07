using APCS.Application.Abstractions.Authentication;
using APCS.Common.Constants;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal static class EmailVerificationFactory
{
    /// <summary>
    /// Issues a single-use email verification token for an account awaiting activation.
    /// </summary>
    /// <remarks>
    /// Shared by registration and by a resend request so both produce the same token shape and
    /// lifetime. The caller's IP and user agent are recorded for the same audit reason as on a
    /// refresh token.
    /// </remarks>
    public static EmailVerification Create(
        IJwtService jwtService,
        Guid userId,
        DateTimeOffset utcNow,
        RequestContext requestContext)
    {
        var token = jwtService.GenerateEmailVerificationToken();
        var tokenHash = jwtService.HashEmailVerificationToken(token);
        var expiresAtUtc = jwtService.GetEmailVerificationExpiresAt(utcNow);
        var entity = AuthToken.CreateSingleUseToken(
            userId,
            AuthTokenTypes.EmailVerification,
            tokenHash,
            expiresAtUtc,
            utcNow,
            requestContext.IpAddress,
            requestContext.UserAgent);

        return new EmailVerification(token, entity);
    }
}
