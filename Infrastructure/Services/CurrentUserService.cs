using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using APCS.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Http;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Reads current user information from the HTTP context, or, in a background worker's scope, the seller the worker
/// acts for (<see cref="ActingUserContext"/>).
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, ActingUserContext? acting = null) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            if (acting?.UserId is { } actingUserId)
                return actingUserId;

            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    /// <inheritdoc />
    public string? Email => acting?.UserId is not null
        ? null
        : httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
            ?? httpContextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Email);

    /// <inheritdoc />
    public bool IsAuthenticated => acting?.UserId is not null || httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
