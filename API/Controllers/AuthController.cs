using APCS.Api.Extensions;
using APCS.Application.Features.Auth;
using APCS.Application.Features.Auth.Common;
using APCS.Application.Features.Auth.Dtos.Request;
using APCS.Application.Features.Auth.Dtos.Response;
using APCS.Common.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides authentication endpoints.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>
    /// Verifies an account email from the token in the verification link.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.VerifyEmailAsync(request, cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Sends a replacement verification email.
    /// </summary>
    /// <remarks>
    /// Answers the same way whether or not the address is registered, so it cannot be used to
    /// discover accounts.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendVerification(
        ResendVerificationEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await authService.ResendVerificationEmailAsync(
            request with { Context = GetRequestContext() },
            cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Refreshes the current access token.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[AuthConstants.RefreshTokenCookieName];
        var result = await authService.RefreshTokenAsync(refreshToken, GetRequestContext(), cancellationToken);

        if (result.IsFailure)
        {
            ClearRefreshTokenCookie();
            return result.ToActionResult(this);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Ok(result.Value);
    }

    /// <summary>
    /// Revokes the current refresh token.
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[AuthConstants.RefreshTokenCookieName];
        var result = await authService.LogoutAsync(refreshToken, GetRequestContext(), cancellationToken);

        ClearRefreshTokenCookie();

        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }

    /// <summary>
    /// Gets the current authenticated user.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await authService.GetCurrentUserAsync(cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Captures the caller's network details for the audit trail on issued tokens.
    /// </summary>
    /// <remarks>
    /// Always taken from the connection, never from the request body, so a caller cannot choose
    /// what gets recorded against its own session.
    /// </remarks>
    private RequestContext GetRequestContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null);

    private void SetRefreshTokenCookie(string refreshToken, DateTimeOffset expiresAtUtc)
    {
        Response.Cookies.Append(
            AuthConstants.RefreshTokenCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = expiresAtUtc,
                Path = "/"
            });
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(AuthConstants.RefreshTokenCookieName, new CookieOptions
        {
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }
}
