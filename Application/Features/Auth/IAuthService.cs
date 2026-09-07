using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.Auth;

/// <summary>
/// Provides the Auth feature's use cases.
/// </summary>
public interface IAuthService
{
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
