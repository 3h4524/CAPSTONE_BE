using APCS.Application.Abstractions.Authentication.Dtos;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Auth.Common;

internal sealed record AuthSession(
    JwtTokenResultDto AccessToken,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    AuthToken Entity);
