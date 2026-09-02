using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using APCS.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Reads current user information from the HTTP context.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public int? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return int.TryParse(value, out var sellerId) ? sellerId : null;
        }
    }

    /// <inheritdoc />
    public string? Email => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Email);

    /// <inheritdoc />
    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
