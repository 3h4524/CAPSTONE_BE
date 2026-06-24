using APCS.Application.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.UseCases.Auth.Common;

internal sealed record AuthSession(
    JwtTokenResult AccessToken,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    RefreshToken Entity);
