using APCS.Application.Abstractions.Authentication.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal sealed record AuthSession(
    JwtTokenResult AccessToken,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    RefreshToken Entity);
