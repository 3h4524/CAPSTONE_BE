using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Auth;

/// <summary>
/// Provides the Auth feature's use cases.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new account and emails its verification link.
    /// </summary>
    Task<Result<RegisterResponseDto>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems an email verification token and activates the account.
    /// </summary>
    Task<Result> VerifyEmailAsync(
        VerifyEmailRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a fresh verification email for an account that is still pending verification.
    /// </summary>
    Task<Result> ResendVerificationEmailAsync(
        ResendVerificationEmailRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using a refresh token and rotates it.
    /// </summary>
    Task<Result<RefreshTokenResponseDto>> RefreshTokenAsync(
        string? refreshToken,
        RequestContext? context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the current refresh token session.
    /// </summary>
    Task<Result> LogoutAsync(
        string? refreshToken,
        RequestContext? context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current authenticated user.
    /// </summary>
    Task<Result<AuthenticatedUserResponse>> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);
}
