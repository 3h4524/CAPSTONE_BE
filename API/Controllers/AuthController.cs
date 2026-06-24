using APCS.Api.Extensions;
using APCS.Application.UseCases.Auth.UC01_Register;
using APCS.Application.UseCases.Auth.UC02_Login;
using APCS.Application.UseCases.Auth.UC02b_RefreshToken;
using APCS.Application.UseCases.Auth.UC02c_Logout;
using APCS.Application.UseCases.Auth.UC02d_GetCurrentUser;
using APCS.Common.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace APCS.Api.Controllers;

/// <summary>
/// Provides authentication endpoints.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Registers a new account.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RegisterCommand(request.Email, request.Password, request.FullName, GetIpAddress()),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult(this);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Ok(result.Value);
    }

    /// <summary>
    /// Logs in with email and password.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new LoginCommand(request.Email, request.Password, GetIpAddress()),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult(this);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);

        return Ok(result.Value);
    }

    /// <summary>
    /// Refreshes the current access token.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[AuthConstants.RefreshTokenCookieName];
        var result = await sender.Send(new RefreshTokenCommand(refreshToken, GetIpAddress()), cancellationToken);

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
        var result = await sender.Send(new LogoutCommand(refreshToken, GetIpAddress()), cancellationToken);

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
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return result.ToActionResult(this);
    }

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

    private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Represents a register request.
/// </summary>
public sealed record RegisterRequest(string Email, string Password, string? FullName);

/// <summary>
/// Represents a login request.
/// </summary>
public sealed record LoginRequest(string Email, string Password);
